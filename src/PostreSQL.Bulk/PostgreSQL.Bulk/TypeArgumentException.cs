using System;

namespace PostgreSQL.Bulk
{
    /// <summary>
    /// Represents an error which occur when an invalid type argument gets passed to a generic method.
    /// </summary>
    [Serializable]
    public class TypeArgumentException : Exception
    {
        /// <inheritdoc/>
        public TypeArgumentException() { }
        /// <inheritdoc/>
        public TypeArgumentException(string message) : base(message) { }
        /// <inheritdoc/>
        public TypeArgumentException(string message, Exception inner) : base(message, inner) { }
        /// <inheritdoc/>
        protected TypeArgumentException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
