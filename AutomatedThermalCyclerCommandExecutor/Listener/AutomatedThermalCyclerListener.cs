using com.apldbio.pcr.instrument;
using com.apldbio.pcr.instrument.@event;
using Serilog;
using System;
using System.ComponentModel;

namespace AutomatedThermalCyclerCommandExecutor.Listener
{
    /// <summary>
    /// The listener of Automated Theraml Cycler
    /// </summary>
    public class AutomatedThermalCyclerListener : InstrumentListener, INotifyPropertyChanged
    {
        private long _elapsedTime;
        /// <summary>
        /// The elapsed time since a protocol started to run.
        /// </summary>
        public long ElapsedTime
        {
            get => _elapsedTime;
            private set
            {
                if (value == _elapsedTime) return;
                _elapsedTime = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ElapsedTime)));
            }
        }

        private long _remainingTime;
        /// <summary>
        /// The remaining time until the protocol completes.
        /// </summary>
        public long RemainingTime
        {
            get => _remainingTime;
            private set
            {
                if (value == _remainingTime) return;
                _remainingTime = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingTime)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Executed when the protocol completes.
        /// </summary>
        public event EventHandler ProtocolCompleted;

        /// <summary>
        /// Executed when the protocol is aborted gracefully.
        /// </summary>
        public event EventHandler ProtocolAborted;

        /// <summary>
        /// Executed when an error occurs in the run.
        /// </summary>
        public event EventHandler ProtocolAbortedByError;

        /// <summary>
        /// Executed when all operations including cleanup has ended.
        /// </summary>
        public event EventHandler ProtocolEnded;

        /// <summary>
        /// Executed when the connection is lost.
        /// </summary>
        public event EventHandler ConnectionLost;

        /// <summary>
        /// The event listener for handling the ConnectionLostEvent.
        /// </summary>
        /// <param name="cle"></param>
        public void handleConnectionLostEvent(ConnectionLostEvent cle)
        {
            Log.Error(cle.toString());
            if (ConnectionLost != null)
            {
                ConnectionLost(this, null);
            }
        }

        public void handleErrorEvent(ErrorEvent ee)
        {
            Log.Error(ee.toString());
        }

        /// <summary>
        /// The event listener for handling the RunEvent.
        /// RunEvent is triggered during a run under conditions such as the run complete, abort, and others.
        /// </summary>
        /// <param name="re"></param>
        public void handleRunEvent(RunEvent re)
        {
            Log.Debug(re.toString());
            var runState = re.getRunState();
            if (runState == RunEvent.RunEventType.COMPLETED)
            {
                if (ProtocolCompleted != null)
                {
                    ProtocolCompleted(this, null);
                }
            }
            else if(runState == RunEvent.RunEventType.ABORTED)
            {
                if (ProtocolAborted != null)
                {
                    ProtocolAborted(this, null);
                }
            }
            else if (runState == RunEvent.RunEventType.ERROR)
            {
                Log.Error(re.toString());
                ResetRunInfo();
                if (ProtocolAbortedByError != null)
                {
                    ProtocolAbortedByError(this, null);
                }
            }
            else if (runState == RunEvent.RunEventType.ENDED)
            {
                ResetRunInfo();
                if (ProtocolEnded != null)
                {
                    ProtocolEnded(this, null);
                }
            }
        }

        public void handleRunTimeEvent(RunTimeEvent rte)
        {
            Log.Debug(rte.toString());

            ElapsedTime = rte.getElapsedTime();

            // Stop at 1 while the instrument state is running.
            // Set to 0 when the instrument state changes to idle (i.e., after the run and cleanup have finished).
            var remainingTime = rte.getRemainingTime();
            if (remainingTime <= 0)
            {
                RemainingTime = 1;
            }
            else
            {
                RemainingTime = remainingTime;
            }
        }

        public void handleTemperatureEvent(TemperatureEvent te)
        {
            Log.Debug(te.toString());
        }

        public void handleDiagnosticsEvent(DiagnosticsEvent sdte)
        {
            Log.Debug(sdte.getDiagnosticsMessage());
        }

        private void ResetRunInfo()
        {
            ElapsedTime = 0;
            RemainingTime = 0;
        }
    }
}
