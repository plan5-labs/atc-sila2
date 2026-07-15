using System;
using System.Runtime.Serialization;

namespace AutomatedThermalCyclerCommandExecutor.Exceptions
{
    class UnexpectedValueReturnedException : Exception
    {
        public UnexpectedValueReturnedException() : base() { }

        public UnexpectedValueReturnedException(string message) : base(message) { }

        public UnexpectedValueReturnedException(string message, Exception innerException) : base (message, innerException) { }

        protected UnexpectedValueReturnedException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
