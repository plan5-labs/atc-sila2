using System;
using System.Runtime.Serialization;

namespace AutomatedThermalCyclerCommandExecutor.Exceptions
{
    public class InvalidConfigException : Exception
    {
        public InvalidConfigException() : base() { }

        public InvalidConfigException(string message) : base(message) { }

        public InvalidConfigException(string message, Exception innerException) : base(message, innerException) { }

        protected InvalidConfigException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
