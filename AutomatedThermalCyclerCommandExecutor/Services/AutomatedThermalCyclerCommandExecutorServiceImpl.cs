using com.apldbio.pcr.exception;
using com.apldbio.pcr.instrument;
using com.apldbio.pcr.protocol;
using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using static AutomatedThermalCyclerCommandExecutor.AutomatedThermalCyclerCommandExecutorService;
using Serilog;
using System.IO;
using AutomatedThermalCyclerCommandExecutor.Listener;
using AutomatedThermalCyclerCommandExecutor.Model;
using System.ComponentModel;
using AutomatedThermalCyclerCommandExecutor.Exceptions;

namespace AutomatedThermalCyclerCommandExecutor.Services
{
    public class AutomatedThermalCyclerCommandExecutorServiceImpl : AutomatedThermalCyclerCommandExecutorServiceBase
    {
        private static readonly AutomatedThermalCyclerCommandExecutorServiceImpl _instance
            = new AutomatedThermalCyclerCommandExecutorServiceImpl();

        // For subscribe status
        private static AutomatedThermalCyclerStatus s_status = AutomatedThermalCyclerStatus.GetInstance();
        private readonly Channel<SubscribeStatusResponse> _statusQueue = System.Threading.Channels.Channel.CreateUnbounded<SubscribeStatusResponse>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        // For subscribe elapsed time
        private readonly Channel<SubscribeElapsedTimeResponse> _elapsedTimeQueue = System.Threading.Channels.Channel.CreateUnbounded<SubscribeElapsedTimeResponse>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        // For subscribe remaining time
        private readonly Channel<SubscribeRemainingTimeResponse> _remainingTimeQueue = System.Threading.Channels.Channel.CreateUnbounded<SubscribeRemainingTimeResponse>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        /// <summary>
        /// An instance to operate the Automated Thermal Cycler (ATC).
        /// </summary>
        private Instrument _atc;

        /// <summary>
        /// An instance of the listener for handling the events from the ATC.
        /// </summary>
        private AutomatedThermalCyclerListener _listener;

        /// <summary>
        /// Already initialized or not.
        /// </summary>
        private static bool s_isInitialized = false;

        /// <summary>
        /// The host name of the ATC.
        /// </summary>
        private static string s_hostName;

        /// <summary>
        /// The serial number of the ATC.
        /// </summary>
        private static string s_serialNumber;

        /// <summary>
        /// The port number for connection to the ATC.
        /// </summary>
        private static string s_port;

        /// <summary>
        /// The loaded protocol information.
        /// </summary>
        private static RunProtocol s_currentLoadedProtocol = null;

        /// <summary>
        /// The loaded protocol has been verified valid or not.
        /// </summary>
        private static bool s_protocolValidated = false;

        #region Instrument State Constants
        private const int INSTRUMENT_STATE_DIAGNOSTICS = 4;
        private const int INSTRUMENT_STATE_ERROR = 3;
        private const int INSTRUMENT_STATE_IDLE = 0;
        private const int INSTRUMENT_STATE_RUNNING = 2;
        private const int INSTRUMENT_STATE_STANDBY = 1;
        #endregion

        // PCR error codes indicating that protocol is invalid.
        private static readonly List<PCRErrorCode> PROTOCOL_INVALID_ERROR_CODES
                    = new List<PCRErrorCode> { PCRErrorCode.PROTOCOL_COVER_TEMPERATURE_OUT_OF_RANGE, PCRErrorCode.PROTOCOL_EMPTY, PCRErrorCode.PROTOCOL_READ_FAILED,
                                               PCRErrorCode.PROTOCOL_SAMPLE_VOLUME_OUT_OF_RANGE, PCRErrorCode.PROTOCOL_TOO_MANY_STAGES, PCRErrorCode.PROTOCOL_WRITE_FAILED };

        private AutomatedThermalCyclerCommandExecutorServiceImpl() { }

        ~AutomatedThermalCyclerCommandExecutorServiceImpl()
        {
            _statusQueue.Writer.TryComplete();
            _elapsedTimeQueue.Writer.TryComplete();
            _remainingTimeQueue.Writer.TryComplete();
        }

        public override Task<LoadResponse> Load(LoadRequest request, ServerCallContext context)
        {
            Log.Information("Load start.");

            CheckInitialized();
            CheckCurrentStatusValid(AutomatedThermalCyclerStatus.StatusCode.Idle);

            byte[] fileData = request.ProtocolFileData.ToByteArray();
            string filePath = string.Empty;
            java.io.File javaFile = null;
            try
            {
                // Create temp file and then load it as java.io.File.
                filePath = Path.GetTempFileName();
                using (var file = File.OpenWrite(filePath))
                {
                    file.Write(fileData, 0, fileData.Length);
                }
                javaFile = new java.io.File(filePath);
                s_currentLoadedProtocol = ProtocolUtil.load(javaFile);
                s_protocolValidated = false;
            }
            catch (PCRException ex)
            {
                // Do not change the status to "Error", as the protocol file may be the cause of exception.
                string errorAbstraction;
                StatusCode rpcStatusCode;
                PCRErrorCode pcrErrorCode = ex.getErrorCode();

                if (PROTOCOL_INVALID_ERROR_CODES.Contains(pcrErrorCode))
                {
                    // Definitively protocol is invalid.
                    errorAbstraction = "InvalidProtocol: The loaded protocol file is invalid.";
                    rpcStatusCode = StatusCode.InvalidArgument;
                }
                else
                {
                    // It is unclear whether the problem is with the instrument or the protocol.
                    errorAbstraction = "Failed to load the protocol file. Please check the instrument and the protocol file.";
                    rpcStatusCode = StatusCode.Internal;
                }

                var errorDetail = "ErrorCode:" + pcrErrorCode + ", ErrorMessage:" + ex.getMessage();
                Log.Error(ex, errorAbstraction);
                Log.Error(errorDetail);
                // Return an error code and message so that the operator of the client side can determine what is wrong with the protocol.
                throw new RpcException(new Status(rpcStatusCode, errorAbstraction + "\n" + errorDetail));
            }
            finally
            {
                if (filePath != null)
                {
                    // Delete temp file.
                    if (javaFile != null)
                    {
                        javaFile.delete();
                    }
                    else
                    {
                        File.Delete(filePath);
                    }
                }
            }

            Log.Information("Load completed.");
            return Task.FromResult(new LoadResponse());
        }

        public override Task<ValidateResponse> Validate(ValidateRequest request, ServerCallContext context)
        {
            Log.Information("Validate start.");
            Log.Information($"MaxSampleVolume: {request.MaxSampleVolume}");

            CheckInitialized();
            CheckCurrentStatusValid(AutomatedThermalCyclerStatus.StatusCode.Idle);

            if (s_currentLoadedProtocol is null)
            {
                var errorMessage = "ProtocolNotLoaded: Execute Load command first.";
                Log.Error(errorMessage);
                throw new RpcException(new Status(StatusCode.FailedPrecondition, errorMessage));
            }

            double maxSampleVolume = request.MaxSampleVolume;
            try
            {
                if (maxSampleVolume == 0)
                {
                    // The default max sample volume is specified.
                    ProtocolUtil.validate(s_currentLoadedProtocol);
                }
                else
                {
                    ProtocolUtil.validate(s_currentLoadedProtocol, maxSampleVolume);
                }
            }
            catch (PCRException ex)
            {
                // Do not change the status to "Error", as the protocol data is the cause of exception.
                string errorAbstraction;
                StatusCode rpcStatusCode;
                PCRErrorCode pcrErrorCode = ex.getErrorCode();

                if (PROTOCOL_INVALID_ERROR_CODES.Contains(pcrErrorCode))
                {
                    // Definitively protocol is invalid.
                    errorAbstraction = "InvalidProtocol: The loaded protocol file is invalid.";
                    rpcStatusCode = StatusCode.InvalidArgument;
                }
                else
                {
                    // It is unclear whether the problem is with the instrument or the protocol.
                    errorAbstraction = "Failed to validate the protocol file. Please check the instrument and the protocol file.";
                    rpcStatusCode = StatusCode.Internal;
                }

                var errorDetail = "ErrorCode:" + pcrErrorCode + ", ErrorMessage:" + ex.getMessage();
                Log.Error(ex, errorAbstraction);
                Log.Error(errorDetail);
                // Return an error code and message so that the operator of the client side can determine what is wrong with the protocol.
                throw new RpcException(new Status(rpcStatusCode, errorAbstraction + "\n" + errorDetail));
            }

            s_protocolValidated = true;
            Log.Information("Validate complete.");
            return Task.FromResult(new ValidateResponse());
        }

        public override Task<OpenLidResponse> OpenLid(OpenLidRequest request, ServerCallContext context)
        {
            Log.Information("OpenLid start.");

            CheckInitialized();
            CheckCurrentStatusValid(AutomatedThermalCyclerStatus.StatusCode.Idle);

            try
            {
                _atc.openLid();
            }
            catch (PCRException ex)
            {
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Error);

                var errorMessage = "Failed to open lid.";
                Log.Error(ex, errorMessage);
                Log.Error("ErrorCode:" + ex.getErrorCode() + ", ErrorMessage:" + ex.getMessage());
                throw new RpcException(new Status(StatusCode.Internal, errorMessage));
            }

            Log.Information("OpenLid complete.");
            return Task.FromResult(new OpenLidResponse());
        }

        public override Task<CloseLidResponse> CloseLid(CloseLidRequest request, ServerCallContext context)
        {
            Log.Information("CloseLid start.");

            CheckInitialized();
            CheckCurrentStatusValid(AutomatedThermalCyclerStatus.StatusCode.Idle);

            try
            {
                _atc.closeLid();
            }
            catch (PCRException ex)
            {
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Error);

                var errorMessage = "Failed to close lid";
                Log.Error(ex, errorMessage);
                Log.Error("ErrorCode:" + ex.getErrorCode() + ", ErrorMessage:" + ex.getMessage());
                throw new RpcException(new Status(StatusCode.Internal, errorMessage));
            }

            Log.Information("CloseLid complete.");
            return Task.FromResult(new CloseLidResponse());
        }

        public override Task<StartRunResponse> StartRun(StartRunRequest request, ServerCallContext context)
        {
            Log.Information("StartRun start.");

            if (s_currentLoadedProtocol == null)
            {
                var errorMessage = "ProtocolNotLoaded: Execute Load command first, and then call Validate command.";
                Log.Error(errorMessage);
                throw new RpcException(new Status(StatusCode.FailedPrecondition, errorMessage));
            }

            if (!s_protocolValidated)
            {
                var errorMessage = "ProtocolNotValidated: Execute Validate command first.";
                Log.Error(errorMessage);
                throw new RpcException(new Status(StatusCode.FailedPrecondition, errorMessage));
            }

            CheckInitialized();
            CheckCurrentStatusValid(AutomatedThermalCyclerStatus.StatusCode.Idle);

            try
            {
                _atc.startRun(s_currentLoadedProtocol);
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Running);
            }
            catch (PCRException ex)
            {
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Error);

                var errorMessage = "Failed to start running.";
                Log.Error(ex, errorMessage);
                Log.Error("ErrorCode:" + ex.getErrorCode() + ", ErrorMessage:" + ex.getMessage());
                throw new RpcException(new Status(StatusCode.Internal, errorMessage));
            }

            Log.Information("StartRun complete.");
            return Task.FromResult(new StartRunResponse());
        }

        public override Task<StopRunResponse> StopRun(StopRunRequest request, ServerCallContext context)
        {
            Log.Information("StopRun start.");

            CheckInitialized();
            CheckCurrentStatusValid(AutomatedThermalCyclerStatus.StatusCode.Running);

            try
            {
                _atc.stopRun();
            }
            catch (PCRException ex)
            {
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Error);

                var errorMessage = "Failed to stop running.";
                Log.Error(ex, errorMessage);
                Log.Error("ErrorCode:" + ex.getErrorCode() + ", ErrorMessage:" + ex.getMessage());
                throw new RpcException(new Status(StatusCode.Internal, errorMessage));
            }
            // Do not change the status to "Idle" here, as status will be updated within the event handler.

            Log.Information("StopRun complete.");
            return Task.FromResult(new StopRunResponse());
        }

        public override Task<GetInstrumentStateResponse> GetInstrumentState(GetInstrumentStateRequest request, ServerCallContext context)
        {
            Log.Information("GetInstrumentState start.");

            // This command can be executed at any time if connected to the ATC.
            CheckInitialized();

            int instrumentState;
            try
            {
                var instrumentStateEnum = CallGetInstrumentState();
                instrumentState = ConvertInstrumentStateToInteger(instrumentStateEnum);
            }
            catch (Exception)
            {
                // Update the status with "Error" since the instrument state cannot be retrieved properly.
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Error);
                throw;
            }

            Log.Information("GetInstrumentState complete.");
            Log.Information($"InstrumentState: {instrumentState}");
            return Task.FromResult(new GetInstrumentStateResponse { InstrumentState = instrumentState } );
        }

        public override Task<ResetResponse> Reset(ResetRequest request, ServerCallContext context)
        {
            Log.Information("Reset start.");

            CheckInitialized();
            CheckCurrentStatusValid(AutomatedThermalCyclerStatus.StatusCode.Error);

            var currentInstrumentState = CallGetInstrumentState();
            if (currentInstrumentState == InstrumentState.IDLE)
            {
                // Nothing to do.
            }
            else if (currentInstrumentState == InstrumentState.RUNNING)
            {
                try
                {
                    // It is possible to call "stopRun" method whether or not the instrument is running.
                    // Nothing happens if the instrument is not running.
                    _atc.stopRun();
                }
                catch (PCRException ex)
                {
                    s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Error);

                    var errorMessage = "Failed to reset the instrument.";
                    Log.Error(ex, errorMessage);
                    Log.Error("ErrorCode:" + ex.getErrorCode() + ", ErrorMessage:" + ex.getMessage());
                    throw new RpcException(new Status(StatusCode.Internal, errorMessage));
                }
            }
            else if (currentInstrumentState == InstrumentState.ERROR)
            {
                var errorMessage = "The instrument error has not been resolved.";
                Log.Error(errorMessage);
                throw new RpcException(new Status(StatusCode.Internal, errorMessage));
            }
            else
            {
                var errorMessage = $"Unexpected instrument state returned. InstrumentState: {currentInstrumentState}";
                Log.Error(errorMessage);
                throw new UnexpectedValueReturnedException(errorMessage);
            }

            // Update the status here, as the event handler may not be called when the instrument is not running.
            s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Idle);

            Log.Information("Reset complete.");
            return Task.FromResult(new ResetResponse());
        }

        public override async Task SubscribeElapsedTime(SubscribeElapsedTimeRequest request, IServerStreamWriter<SubscribeElapsedTimeResponse> responseStream, ServerCallContext context)
        {
            void handler(object s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(AutomatedThermalCyclerListener.ElapsedTime) && s is AutomatedThermalCyclerListener)
                {
                    _elapsedTimeQueue.Writer.TryWrite(new SubscribeElapsedTimeResponse { ElapsedTime = (s as AutomatedThermalCyclerListener).ElapsedTime });
                }
            }

            // To avoid logging every second, output a log only once when the subscription starts.
            var initialElapsedTime = _listener is null ? 0 : _listener.ElapsedTime;
            Log.Information($"Start to subscribe ElapsedTime. Current ElapsedTime: {initialElapsedTime}");

            _elapsedTimeQueue.Writer.TryWrite(new SubscribeElapsedTimeResponse { ElapsedTime = initialElapsedTime });
            _listener.PropertyChanged += handler;
            try
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    if (await _elapsedTimeQueue.Reader.WaitToReadAsync(context.CancellationToken) && _elapsedTimeQueue.Reader.TryRead(out var update))
                    {
                        await responseStream.WriteAsync(update);
                    }
                }
            }
            finally
            {
                s_status.PropertyChanged -= handler;
                Log.Information("Stop subscribing ElapsedTime.");
            }
        }

        public override async Task SubscribeRemainingTime(SubscribeRemainingTimeRequest request, IServerStreamWriter<SubscribeRemainingTimeResponse> responseStream, ServerCallContext context)
        {
            void handler(object s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(AutomatedThermalCyclerListener.RemainingTime) && s is AutomatedThermalCyclerListener)
                {
                    _remainingTimeQueue.Writer.TryWrite(new SubscribeRemainingTimeResponse { RemainingTime = (s as AutomatedThermalCyclerListener).RemainingTime });
                }
            }

            // To avoid logging every second, output a log only once when the subscription starts.
            var initialRemainingTime = _listener is null ? 0 : _listener.RemainingTime;
            Log.Information($"Start to subscribe RemainingTime. Current RemainingTime: {initialRemainingTime}");

            _remainingTimeQueue.Writer.TryWrite(new SubscribeRemainingTimeResponse { RemainingTime = initialRemainingTime });
            _listener.PropertyChanged += handler;
            try
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    if (await _remainingTimeQueue.Reader.WaitToReadAsync(context.CancellationToken) && _remainingTimeQueue.Reader.TryRead(out var update))
                    {
                        await responseStream.WriteAsync(update);
                    }
                }
            }
            finally
            {
                _listener.PropertyChanged -= handler;
                Log.Information("Stop subscribing RemainingTime.");
            }
        }

        public override async Task SubscribeStatus(SubscribeStatusRequest request, IServerStreamWriter<SubscribeStatusResponse> responseStream, ServerCallContext context)
        {
            void handler(object s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(AutomatedThermalCyclerStatus.Status) && s is AutomatedThermalCyclerStatus)
                {
                    _statusQueue.Writer.TryWrite(new SubscribeStatusResponse { Status = (int)(s as AutomatedThermalCyclerStatus).Status });
                }
            }

            Log.Information($"Start to subscribe Status. Current Status: {s_status.Status}");

            _statusQueue.Writer.TryWrite(new SubscribeStatusResponse { Status = (int)s_status.Status });
            s_status.PropertyChanged += handler;
            try
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    if (await _statusQueue.Reader.WaitToReadAsync(context.CancellationToken) && _statusQueue.Reader.TryRead(out var update))
                    {
                        await responseStream.WriteAsync(update);
                    }
                }
            }
            finally
            {
                s_status.PropertyChanged -= handler;
                Log.Information("Stop subscribing Status.");
            }
        }

        #region Service Basics
        public static AutomatedThermalCyclerCommandExecutorServiceImpl GetService()
        {
            return _instance;
        }

        public void Initialize(IConfigurationSection configuration)
        {
            s_hostName = configuration["HostName"];
            s_serialNumber = configuration["SerialNumber"];
            s_port = configuration["Port"];
            Log.Information($"HostName read: {s_hostName}");
            Log.Information($"SerialNumber read: {s_serialNumber}");
            Log.Information($"Port read: {s_port}");

            if (string.IsNullOrEmpty(s_hostName))
            {
                Log.Debug("HostName is empty.");

                // If the hostname is null, get hostname by the serial number.
                var finder = new InstrumentFinder();
                s_hostName = finder.find(s_serialNumber, 5000);

                if (string.IsNullOrEmpty(s_hostName))
                {
                    Log.Error("Failed to get hostname by the serial number.");
                    throw new InvalidConfigException("Failed to get hostname. Please check the configuration file.");
                }
            }

            if (!int.TryParse(s_port, out int _))
            {
                var errorMessage = "Invalid port number. Port number must be integer.";
                Log.Error(errorMessage);
                throw new InvalidConfigException(errorMessage);
            }

            Connect();
        }

        public void Close()
        {
            Log.Debug("Close the connection to the Automated Thermal Cycler.");

            try
            {
                if (_atc != null)
                {
                    DisposeListener();
                    _atc.disconnect();
                    Log.Information("Disconnect the Automated Thermal Cycler successfully.");
                }
            }
            catch (PCRException ex)
            {
                Log.Error(ex, "Failed to disconnect the Automated Thermal Cycler.");
                Log.Error("ErrorCode:" + ex.getErrorCode() + ", ErrorMessage:" + ex.getMessage());
            }
            finally
            {
                _listener = null;
                _atc = null;
                s_isInitialized = false;
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.NotConnected);
            }

            Log.Debug("Succeed to close the connection to the Automated Thermal Cycler.");
        }

        /// <summary>
        /// Connect to the ATC.
        /// </summary>
        private void Connect()
        {
            Log.Debug("Connect to the Automated Thermal Cycler.");

            try
            {
                _atc = new AutoThermalCycler(s_hostName);
                // Verified in Initialize method that the following conversion is possible.
                int port = int.Parse(s_port);

                // Set the serial number and the port number for the SSL communication.
                // The instrument only accepts SSL connections.
                _atc.setPort(port);
                _atc.setSerialNumber(s_serialNumber);
                _atc.connect();

                // Listener
                _listener = new AutomatedThermalCyclerListener();
                _listener.ProtocolCompleted += ProtocolCompleted;
                _listener.ProtocolAborted += ProtocolAborted;
                _listener.ProtocolAbortedByError += ProtocolAbortedByError;
                _listener.ProtocolEnded += ProtocolEnded;
                _listener.ConnectionLost += ConnectionLost;
                _atc.addListener(_listener);
            }
            catch (PCRException ex)
            {
                _atc = null;
                Log.Error(ex, "Failed to connect the Automated Thermal Cycler");
                Log.Error("ErrorCode:" + ex.getErrorCode() + ", ErrorMessage:" + ex.getMessage());
                return;
            }
            s_isInitialized = true;
            s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Idle);

            Log.Debug("Succeed to establish the connection to the Automated Thermal Cycler.");
        }

        /// <summary>
        /// Check the ATC has been already initialized or not.
        /// If not initialized, retry to connect to the ATC.
        /// </summary>
        private void CheckInitialized()
        {
            if (!s_isInitialized)
            {
                Log.Information("Retrying to establish the connection to the ATC.");
                Connect();
                if (!s_isInitialized)
                {
                    var errorMessage = "Automated Thermal Cycler is not initialized.";
                    Log.Error(errorMessage);
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, errorMessage));
                }
            }
        }

        private void DisposeListener()
        {
            _atc?.removeListener(_listener);

            _listener.ProtocolCompleted -= ProtocolCompleted;
            _listener.ProtocolAborted -= ProtocolAborted;
            _listener.ProtocolAbortedByError -= ProtocolAbortedByError;
            _listener.ProtocolEnded -= ProtocolEnded;
            _listener.ConnectionLost -= ConnectionLost;
            _listener = null;
        }

        /// <summary>
        /// Call getInstrumentState method of the ATC.
        /// </summary>
        /// <returns>Current instrument state</returns>
        /// <remark>Prerequisite checks and status updates must be performed within the command-handling methods.</remark>
        private InstrumentState CallGetInstrumentState()
        {
            InstrumentState state;
            try
            {
                state = _atc.getInstrumentState();
            }
            catch (PCRException ex)
            {
                var errorMessage = "Failed to get instrument state.";
                Log.Error(ex, errorMessage);
                Log.Error("ErrorCode:" + ex.getErrorCode() + ", ErrorMessage:" + ex.getMessage());
                throw new RpcException(new Status(StatusCode.Internal, errorMessage));
            }

            Log.Debug($"Current instrument state: {state}");
            return state;
        }
        #endregion

        #region EventHandler
        private void ProtocolCompleted(object sender, EventArgs e)
        {
            Log.Information("The protocol complete.");
            // Do not change the status here, as status will be updated within the ProtocolEnded event handler.
        }

        private void ProtocolAborted(object sender, EventArgs e)
        {
            Log.Information("The protocol is aborted.");
            // Do not change the status here, as status will be updated within the ProtocolEnded event handler.
        }

        private void ProtocolAbortedByError(object sender, EventArgs e)
        {
            Log.Information("The protocol is aborted by an error.");
            s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Error);
        }

        private void ProtocolEnded(object sender, EventArgs e)
        {
            Log.Information("The protocol and cleanup works ended.");
            if (s_status.Status != AutomatedThermalCyclerStatus.StatusCode.Error)
            {
                // Do not change the status if the status is "Error".
                // Only "Reset" command can restore the instrument from the "Error" status.
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Idle);
            }
        }

        private void ConnectionLost(object sender, EventArgs e)
        {
            Log.Information("Connection is losted.");
            DisposeListener();
            _atc = null;
            s_isInitialized = false;
            s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.NotConnected);
        }
        #endregion

        /// <summary>
        /// Convert the instrument state defined as a Java Enum value to an integer.
        /// </summary>
        /// <param name="stateEnum">The instrument state as a Java Enum value</param>
        /// <returns>The instrument state as a integer</returns>
        private static int ConvertInstrumentStateToInteger(InstrumentState stateEnum)
        {
            int state;
            if (stateEnum == InstrumentState.DIAGNOSTICS)
            {
                state = INSTRUMENT_STATE_DIAGNOSTICS;
            }
            else if (stateEnum == InstrumentState.ERROR)
            {
                state = INSTRUMENT_STATE_ERROR;
            }
            else if (stateEnum == InstrumentState.IDLE)
            {
                state = INSTRUMENT_STATE_IDLE;
            }
            else if (stateEnum == InstrumentState.RUNNING)
            {
                state = INSTRUMENT_STATE_RUNNING;
            }
            else if (stateEnum == InstrumentState.STANDBY)
            {
                state = INSTRUMENT_STATE_STANDBY;
            }
            else if (stateEnum == InstrumentState.COMMANDERROR)
            {
                // It is unexpected behavior for "getInstrumentState" method to return COMMANDERROR without throwing any exceptions.
                Log.Error($"Failed to get instrument state. InstrumentStateEnum: {stateEnum}");
                throw new UnexpectedValueReturnedException("Unexpected error occurred in the \"getInstrumentState\" method.");
            }
            else
            {
                Log.Error($"Failed to convert instrument state enum into integer. InstrumentStateEnum: {stateEnum}");
                throw new ArgumentOutOfRangeException("Failed to convert instrument state enum into integer. Unexpected instrument state enum has been passed.");
            }

            Log.Debug($"Converted instrument state: {state}");
            return state;
        }

        private static void UpdateStatusAccordingToInstrumentState(InstrumentState state)
        {
            if (state == InstrumentState.DIAGNOSTICS)
            {
                // Treat as Running during diagnostics.
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Running);
            }
            else if (state == InstrumentState.ERROR)
            {
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Error);
            }
            else if (state == InstrumentState.IDLE)
            {
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Idle);
            }
            else if (state == InstrumentState.RUNNING)
            {
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Running);
            }
            else if (state == InstrumentState.STANDBY)
            {
                // This instrument never enter the STANDBY state because this application does not call "standBy" method.
                // If the "StandBy" command is implemented, determine how STANDBY state correspondes to the status and update the status.
                // In that case, add processing to call "powerOn" method in Reset command.
                Log.Error($"Unexpected instrument state received. InstrumentStateEnum: {state}");
                throw new UnexpectedValueReturnedException("Unexpected error occurred in the \"getInstrumentState\" method.");
            }
            else if (state == InstrumentState.COMMANDERROR)
            {
                // It is unexpected behavior for "getInstrumentState" method to return COMMANDERROR without throwing any exceptions.
                Log.Error($"Failed to get instrument state. InstrumentStateEnum: {state}");
                throw new UnexpectedValueReturnedException("Unexpected error occurred in the \"getInstrumentState\" method.");
            }
            else
            {
                Log.Error($"Failed to convert instrument state enum into integer. InstrumentStateEnum: {state}");
                throw new ArgumentOutOfRangeException("Failed to convert instrument state enum into integer. Unexpected instrument state enum has been passed.");
            }
        }

        /// <summary>
        /// Update status according to the current instruent state, then check the status is valid for command execution.
        /// </summary>
        /// <param name="validStatuses"></param>
        /// <remarks>Call this method before executing command.</remarks>
        private void CheckCurrentStatusValid(List<AutomatedThermalCyclerStatus.StatusCode> validStatuses)
        {
            var statusesStr = string.Join(", ", validStatuses.Select(s => Enum.GetName(typeof(AutomatedThermalCyclerStatus.StatusCode), s)));
            Log.Debug($"Check current status is valid or not. Expected: {statusesStr}");

            // Update status according to the current instrument state.
            try
            {
                var state = CallGetInstrumentState();
                UpdateStatusAccordingToInstrumentState(state);
            }
            catch (Exception)
            {
                // Update the status with "Error" since the instrument state cannot be retrieved.
                s_status.UpdateStatus(AutomatedThermalCyclerStatus.StatusCode.Error);
                throw;
            }

            // Check the status for command execution.
            if (s_status.IsStatusValid(validStatuses))
            {
                // Current status is valid.
                Log.Debug("Current status is valid.");
                return;
            }

            // Current status is invalid.
            var errorMessage = $"InvalidStatus. Expected: {statusesStr} / Actual: {Enum.GetName(typeof(AutomatedThermalCyclerStatus.StatusCode), s_status.Status)}";
            Log.Error(errorMessage);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, errorMessage));
        }

        /// <summary>
        /// Update status according to the current instruent state, then check the status is valid for command execution.
        /// </summary>
        /// <param name="validStatus"></param>
        /// <remarks>Call this method before executing command.</remarks>
        private void CheckCurrentStatusValid(AutomatedThermalCyclerStatus.StatusCode validStatus)
        {
            CheckCurrentStatusValid(new List<AutomatedThermalCyclerStatus.StatusCode> { validStatus });
        }
    }
}
