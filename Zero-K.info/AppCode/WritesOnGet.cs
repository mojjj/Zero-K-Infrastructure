using System;

namespace ZeroKWeb
{
    /// <summary>
    /// An action that writes to the database although a GET can reach it, and is meant to.
    ///
    /// The usual case is a page recording that you looked at it - UpdateLastRead on a forum
    /// thread, the galaxy render cache. There is nothing to forge: the request does what the
    /// visitor's own browsing would have done anyway.
    ///
    /// tools/check-get-writes.py requires one of these or <see cref="WritesOnGetNotYetFixedAttribute"/>
    /// on every such action, so the reason sits on the method rather than in a list somewhere else.
    /// Nothing reads either at runtime; they exist to be read by a person and by that script.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WritesOnGetByDesignAttribute : Attribute
    {
        public WritesOnGetByDesignAttribute(string reason) { Reason = reason; }
        public string Reason { get; }
    }

    /// <summary>
    /// An action that writes to the database, a GET can reach it, and that is a hole.
    ///
    /// Separate from <see cref="WritesOnGetByDesignAttribute"/> on purpose: the check counts these
    /// and prints them every run, so they are a list that shrinks rather than a decision that has
    /// been made. The reason says what stands in the way, not that it is acceptable.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WritesOnGetNotYetFixedAttribute : Attribute
    {
        public WritesOnGetNotYetFixedAttribute(string reason) { Reason = reason; }
        public string Reason { get; }
    }
}
