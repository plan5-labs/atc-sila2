using Serilog;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace AutomatedThermalCyclerCommandExecutor.Model
{
    public class AutomatedThermalCyclerStatus : INotifyPropertyChanged
    {
        private static AutomatedThermalCyclerStatus _instance = new AutomatedThermalCyclerStatus();

        private AutomatedThermalCyclerStatus() { }

        public static AutomatedThermalCyclerStatus GetInstance()
        {
            return _instance;
        }

        /// <summary>
        /// The status of the instrument. These values are common to all instruments.
        /// </summary>
        public enum StatusCode
        {
            NotConnected = 0,
            Idle = 1,
            Running = 2,
            Error = 3,
        }

        private StatusCode _status;
        public StatusCode Status
        {
            get => _status;
            private set
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        public bool IsStatusValid(StatusCode expectedStatus)
        {
            return IsStatusValid(new List<StatusCode> { expectedStatus });
        }

        public bool IsStatusValid(List<StatusCode> expectedStatuses)
        {
            return expectedStatuses.Any(s => s == Status);
        }

        /// <summary>
        /// Update status.
        /// </summary>
        /// <param name="nextStatus"></param>
        /// <remarks>
        /// This method is intended to be called only in the following location:
        /// - Connect() and Close()
        /// - Methods corresponded to the commands
        /// - Event handlers
        /// - The common process that updates the status based on the current InstrumentState before executing commands
        /// </remarks>
        public void UpdateStatus(StatusCode nextStatus)
        {
            if (nextStatus != Status)
            {
                Log.Information($"Update the status from {Status} to {nextStatus}.");
                Status = nextStatus;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
