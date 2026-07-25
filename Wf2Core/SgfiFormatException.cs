namespace Wf2Core;

/// <summary>Thrown when a file does not match the expected <c>profile.sgfi</c> format.</summary>
public sealed class SgfiFormatException(string message) : Exception(message);
