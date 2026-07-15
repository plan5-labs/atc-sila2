using AutomatedThermalCyclerCommandExecutor;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sila2.Org.Riken.Instruments.Automatedthermalcyclercontroller.V1;
using Sila2.Org.Silastandard;
using SiLA2.Commands;
using SiLA2.Server;
using SiLA2.Server.Utils;
using static Sila2.Org.Riken.Instruments.Automatedthermalcyclercontroller.V1.AutomatedThermalCyclerController;

namespace AutomatedThermalCyclerController.Features.Services
{
    public class AutomatedThermalCyclerControllerService : AutomatedThermalCyclerControllerBase
    {
        private readonly SiLA2.Feature _silaFeature;
        private readonly AutomatedThermalCyclerCommandExecutorService.AutomatedThermalCyclerCommandExecutorServiceClient _executorClient;
        private readonly ILogger _logger;

        // commandManagers
        private IObservableCommandManager<Load_Parameters, Load_Responses> _loadCommandManager;
        private IObservableCommandManager<Validate_Parameters, Validate_Responses> _validateCommandManager;
        private IObservableCommandManager<OpenLid_Parameters, OpenLid_Responses> _openLidCommandManager;
        private IObservableCommandManager<CloseLid_Parameters, CloseLid_Responses> _closeLidCommandManager;
        private IObservableCommandManager<StartRun_Parameters, StartRun_Responses> _startRunCommandManager;
        private IObservableCommandManager<StopRun_Parameters, StopRun_Responses> _stopRunCommandManager;
        private IObservableCommandManager<Reset_Parameters, Reset_Responses> _resetCommandManager;

        private const string NOT_INITIALIZED = "NotInitialized";
        private const string INSTRUMENT_ERROR_OCCURRED = "InstrumentErrorOccurred";
        private const string INVALID_INSTRUMENT_STATUS_ERROR = "InvalidInstrumentStatusError";
        private const string PROTOCOL_NOT_LOADED = "ProtocolNotLoaded";
        private const string PROTOCOL_NOT_VALIDATED = "ProtocolNotValidated";
        private const string INVALID_PROTOCOL = "InvalidProtocol";

        public AutomatedThermalCyclerControllerService(IObservableCommandManager<Load_Parameters, Load_Responses> loadCommandManager,
                                                       IObservableCommandManager<Validate_Parameters, Validate_Responses> validateCommandManager,
                                                       IObservableCommandManager<OpenLid_Parameters, OpenLid_Responses> openLidCommandManager,
                                                       IObservableCommandManager<CloseLid_Parameters, CloseLid_Responses> closeLidCommandManager,
                                                       IObservableCommandManager<StartRun_Parameters, StartRun_Responses> startRunCommandManager,
                                                       IObservableCommandManager<StopRun_Parameters, StopRun_Responses> stopRunCommandManager,
                                                       IObservableCommandManager<Reset_Parameters, Reset_Responses> resetCommandManager,
                                                       ISiLA2Server silaServer,
                                                       IConfiguration configuration,
                                                       ILogger<AutomatedThermalCyclerControllerService> logger)
        {
            // Load feature definition for validation
            _silaFeature = silaServer.ReadFeature(Path.Combine("Features", "AutomatedThermalCyclerController-v1_0.sila.xml"));

            _loadCommandManager = loadCommandManager;
            _validateCommandManager = validateCommandManager;
            _openLidCommandManager = openLidCommandManager;
            _closeLidCommandManager = closeLidCommandManager;
            _startRunCommandManager = startRunCommandManager;
            _stopRunCommandManager = stopRunCommandManager;
            _resetCommandManager = resetCommandManager;

            _logger = logger;

            // Open the channel for Automated Thermal Cycler Command Executor
            var channel = GrpcChannel.ForAddress($"http://localhost:{configuration["CommandExecutorConfig:Port"]}/");
            _executorClient = new AutomatedThermalCyclerCommandExecutorService.AutomatedThermalCyclerCommandExecutorServiceClient(channel);
        }

        #region Load
        public override async Task<CommandConfirmation> Load(Load_Parameters request, ServerCallContext context)
        {
            var command = await _loadCommandManager.AddCommand(request, LoadWorker);
            return command.Confirmation;
        }

        public override async Task Load_Info(CommandExecutionUUID request, IServerStreamWriter<ExecutionInfo> responseStream, ServerCallContext context)
        {
            try
            {
                await _loadCommandManager.RegisterForInfo(request, responseStream, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error occurred: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public override Task<Load_Responses> Load_Result(CommandExecutionUUID request, ServerCallContext context)
        {
            var command = _loadCommandManager.GetCommand(request);
            return Task.FromResult(command.Result);
        }

        /// <summary>
        /// Invoke the Load method on the CommandExecutor.
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="parameters"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private Load_Responses LoadWorker(IProgress<ExecutionInfo> progress, Load_Parameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var response = _executorClient.Load(new LoadRequest
                {
                    ProtocolFileData = parameters.ProtocolFileData.Value
                });
            }
            catch (RpcException ex)
            {
                var errorType = GetErrorType(ex);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateDefinedExecutionError(
                    _silaFeature.GetFullyQualifiedDefinedExecutionErrorIdentifier(errorType),
                    ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }
            
            return new Load_Responses();
        }
        #endregion

        #region Validate
        public override async Task<CommandConfirmation> Validate(Validate_Parameters request, ServerCallContext context)
        {
            var command = await _validateCommandManager.AddCommand(request, ValidateWorker);
            return command.Confirmation;
        }

        public override async Task Validate_Info(CommandExecutionUUID request, IServerStreamWriter<ExecutionInfo> responseStream, ServerCallContext context)
        {
            try
            {
                await _validateCommandManager.RegisterForInfo(request, responseStream, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error occurred: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public override Task<Validate_Responses> Validate_Result(CommandExecutionUUID request, ServerCallContext context)
        {
            var command = _validateCommandManager.GetCommand(request);
            return Task.FromResult(command.Result);
        }

        /// <summary>
        /// Invoke the Validate method on the CommandExecutor.
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="parameters"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private Validate_Responses ValidateWorker(IProgress<ExecutionInfo> progress, Validate_Parameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var response = _executorClient.Validate(new ValidateRequest
                {
                    MaxSampleVolume = parameters.MaxSampleVolume.Value
                });
            }
            catch (RpcException ex)
            {
                var errorType = GetErrorType(ex);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateDefinedExecutionError(
                    _silaFeature.GetFullyQualifiedDefinedExecutionErrorIdentifier(errorType),
                    ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }

            return new Validate_Responses();
        }
        #endregion

        #region OpenLid
        public override async Task<CommandConfirmation> OpenLid(OpenLid_Parameters request, ServerCallContext context)
        {
            var command = await _openLidCommandManager.AddCommand(request, OpenLidWorker);
            return command.Confirmation;
        }

        public override async Task OpenLid_Info(CommandExecutionUUID request, IServerStreamWriter<ExecutionInfo> responseStream, ServerCallContext context)
        {
            try
            {
                await _openLidCommandManager.RegisterForInfo(request, responseStream, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error occurred: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public override Task<OpenLid_Responses> OpenLid_Result(CommandExecutionUUID request, ServerCallContext context)
        {
            var command = _openLidCommandManager.GetCommand(request);
            return Task.FromResult(command.Result);
        }

        /// <summary>
        /// Invoke the OpenLid method on the CommandExecutor.
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="parameters"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private OpenLid_Responses OpenLidWorker(IProgress<ExecutionInfo> progress, OpenLid_Parameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var response = _executorClient.OpenLid(new OpenLidRequest());
            }
            catch (RpcException ex)
            {
                var errorType = GetErrorType(ex);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateDefinedExecutionError(
                    _silaFeature.GetFullyQualifiedDefinedExecutionErrorIdentifier(errorType),
                    ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }

            return new OpenLid_Responses();
        }
        #endregion

        #region CloseLid
        public override async Task<CommandConfirmation> CloseLid(CloseLid_Parameters request, ServerCallContext context)
        {
            var command = await _closeLidCommandManager.AddCommand(request, CloseLidWorker);
            return command.Confirmation;
        }

        public override async Task CloseLid_Info(CommandExecutionUUID request, IServerStreamWriter<ExecutionInfo> responseStream, ServerCallContext context)
        {
            try
            {
                await _closeLidCommandManager.RegisterForInfo(request, responseStream, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error occurred: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public override Task<CloseLid_Responses> CloseLid_Result(CommandExecutionUUID request, ServerCallContext context)
        {
            var command = _closeLidCommandManager.GetCommand(request);
            return Task.FromResult(command.Result);
        }

        /// <summary>
        /// Invoke the CloseLid method on the CommandExecutor.
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="parameters"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private CloseLid_Responses CloseLidWorker(IProgress<ExecutionInfo> progress, CloseLid_Parameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var response = _executorClient.CloseLid(new CloseLidRequest());
            }
            catch (RpcException ex)
            {
                var errorType = GetErrorType(ex);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateDefinedExecutionError(
                    _silaFeature.GetFullyQualifiedDefinedExecutionErrorIdentifier(errorType),
                    ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }

            return new CloseLid_Responses();
        }
        #endregion

        #region StartRun
        public override async Task<CommandConfirmation> StartRun(StartRun_Parameters request, ServerCallContext context)
        {
            var command = await _startRunCommandManager.AddCommand(request, StartRunWorker);
            return command.Confirmation;
        }

        public override async Task StartRun_Info(CommandExecutionUUID request, IServerStreamWriter<ExecutionInfo> responseStream, ServerCallContext context)
        {
            try
            {
                await _startRunCommandManager.RegisterForInfo(request, responseStream, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error occurred: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public override Task<StartRun_Responses> StartRun_Result(CommandExecutionUUID request, ServerCallContext context)
        {
            var command = _startRunCommandManager.GetCommand(request);
            return Task.FromResult(command.Result);
        }

        /// <summary>
        /// Invoke the StartRun method on the CommandExecutor.
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="parameters"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private StartRun_Responses StartRunWorker(IProgress<ExecutionInfo> progress, StartRun_Parameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var response = _executorClient.StartRun(new StartRunRequest());
            }
            catch (RpcException ex)
            {
                var errorType = GetErrorType(ex);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateDefinedExecutionError(
                    _silaFeature.GetFullyQualifiedDefinedExecutionErrorIdentifier(errorType),
                    ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }

            return new StartRun_Responses();
        }
        #endregion

        #region StopRun
        public override async Task<CommandConfirmation> StopRun(StopRun_Parameters request, ServerCallContext context)
        {
            var command = await _stopRunCommandManager.AddCommand(request, StopRunWorker);
            return command.Confirmation;
        }

        public override async Task StopRun_Info(CommandExecutionUUID request, IServerStreamWriter<ExecutionInfo> responseStream, ServerCallContext context)
        {
            try
            {
                await _stopRunCommandManager.RegisterForInfo(request, responseStream, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error occurred: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public override Task<StopRun_Responses> StopRun_Result(CommandExecutionUUID request, ServerCallContext context)
        {
            var command = _stopRunCommandManager.GetCommand(request);
            return Task.FromResult(command.Result);
        }

        /// <summary>
        /// Invoke the StopRun method on the CommandExecutor.
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="parameters"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private StopRun_Responses StopRunWorker(IProgress<ExecutionInfo> progress, StopRun_Parameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var response = _executorClient.StopRun(new StopRunRequest());
            }
            catch (RpcException ex)
            {
                var errorType = GetErrorType(ex);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateDefinedExecutionError(
                    _silaFeature.GetFullyQualifiedDefinedExecutionErrorIdentifier(errorType),
                    ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }

            return new StopRun_Responses();
        }
        #endregion

        #region GetInstrumentState
        public override async Task<GetInstrumentState_Responses> GetInstrumentState(GetInstrumentState_Parameters request, ServerCallContext context)
        {
            int instrumentState = 0;

            try
            {
                var paramForExecutor = new GetInstrumentStateRequest();
                var response = await _executorClient.GetInstrumentStateAsync(paramForExecutor);
                instrumentState = response.InstrumentState;
            }
            catch (RpcException ex)
            {
                var errorType = GetErrorType(ex);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateDefinedExecutionError(
                    _silaFeature.GetFullyQualifiedDefinedExecutionErrorIdentifier(errorType),
                    ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }

            return new GetInstrumentState_Responses { InstrumentState = new Integer { Value = instrumentState } };
        }
        #endregion

        #region Reset
        public override async Task<CommandConfirmation> Reset(Reset_Parameters request, ServerCallContext context)
        {
            var command = await _resetCommandManager.AddCommand(request, ResetWorker);
            return command.Confirmation;
        }

        public override async Task Reset_Info(CommandExecutionUUID request, IServerStreamWriter<ExecutionInfo> responseStream, ServerCallContext context)
        {
            try
            {
                await _resetCommandManager.RegisterForInfo(request, responseStream, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error occurred: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public override Task<Reset_Responses> Reset_Result(CommandExecutionUUID request, ServerCallContext context)
        {
            var command = _resetCommandManager.GetCommand(request);
            return Task.FromResult(command.Result);
        }

        /// <summary>
        /// Invoke the Reset method on the CommandExecutor.
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="parameters"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private Reset_Responses ResetWorker(IProgress<ExecutionInfo> progress, Reset_Parameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var response = _executorClient.Reset(new ResetRequest());
            }
            catch (RpcException ex)
            {
                var errorType = GetErrorType(ex);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateDefinedExecutionError(
                    _silaFeature.GetFullyQualifiedDefinedExecutionErrorIdentifier(errorType),
                    ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }

            return new Reset_Responses();
        }
        #endregion

        public override async Task Subscribe_Status(Subscribe_Status_Parameters request, IServerStreamWriter<Subscribe_Status_Responses> responseStream, ServerCallContext context)
        {
            _logger.LogDebug("Subscribe Status start.");

            var cts = new CancellationTokenSource();
            var callToExecutor = _executorClient.SubscribeStatus(new SubscribeStatusRequest(), cancellationToken: cts.Token);
            try
            {
                await foreach (var update in callToExecutor.ResponseStream.ReadAllAsync(cts.Token))
                {
                    // Notify that the status changed to SiLA Client
                    await responseStream.WriteAsync(new Subscribe_Status_Responses { Status = new Integer { Value = update.Status } });
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Subscribe Status is cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }
        }

        public override async Task Subscribe_ElapsedTime(Subscribe_ElapsedTime_Parameters request, IServerStreamWriter<Subscribe_ElapsedTime_Responses> responseStream, ServerCallContext context)
        {
            _logger.LogDebug("Subscribe ElapsedTime start.");

            var cts = new CancellationTokenSource();
            var callToExecutor = _executorClient.SubscribeElapsedTime(new SubscribeElapsedTimeRequest(), cancellationToken: cts.Token);
            try
            {
                await foreach (var update in callToExecutor.ResponseStream.ReadAllAsync(cts.Token))
                {
                    // Notify that the elapsedTime changed to SiLA Client
                    var elapsedSpan = new TimeSpan(0, 0, (int)update.ElapsedTime);
                    var elapsedStr = elapsedSpan.ToString(@"hh\:mm\:ss");
                    await responseStream.WriteAsync(new Subscribe_ElapsedTime_Responses { ElapsedTime = new Sila2.Org.Silastandard.String { Value = elapsedStr } });
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Subscribe ElapsedTime is cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }
        }

        public override async Task Subscribe_RemainingTime(Subscribe_RemainingTime_Parameters request, IServerStreamWriter<Subscribe_RemainingTime_Responses> responseStream, ServerCallContext context)
        {
            _logger.LogDebug("Subscribe RemainingTime start.");

            var cts = new CancellationTokenSource();
            var callToExecutor = _executorClient.SubscribeRemainingTime(new SubscribeRemainingTimeRequest(), cancellationToken: cts.Token);
            try
            {
                await foreach (var update in callToExecutor.ResponseStream.ReadAllAsync(cts.Token))
                {
                    // Notify that the remainingTime changed to SiLA Client
                    var remainingSpan = new TimeSpan(0, 0, (int)update.RemainingTime);
                    var remainingStr = remainingSpan.ToString(@"hh\:mm\:ss");
                    await responseStream.WriteAsync(new Subscribe_RemainingTime_Responses { RemainingTime = new Sila2.Org.Silastandard.String { Value = remainingStr } });
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Subscribe RemainingTime is cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                ErrorHandling.RaiseSiLAError(ErrorHandling.CreateUndefinedExecutionError(ex.Message));
            }
        }

        private string GetErrorType(RpcException exception)
        {
            switch (exception.StatusCode)
            {
                case StatusCode.FailedPrecondition:
                    if (exception.Message.Contains("InvalidStatus"))
                    {
                        return INVALID_INSTRUMENT_STATUS_ERROR;
                    }
                    else if (exception.Message.Contains("ProtocolNotLoaded"))
                    {
                        return PROTOCOL_NOT_LOADED;
                    }
                    else if (exception.Message.Contains("ProtocolNotValidated"))
                    {
                        return PROTOCOL_NOT_VALIDATED;
                    }
                    return NOT_INITIALIZED;

                case StatusCode.InvalidArgument:
                    return INVALID_PROTOCOL;

                case StatusCode.Internal:
                default:
                    return INSTRUMENT_ERROR_OCCURRED;
            }
        }
    }
}
