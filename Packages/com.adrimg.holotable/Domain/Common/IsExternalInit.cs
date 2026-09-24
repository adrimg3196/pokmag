#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Shim required by the C# 9 compiler to emit <c>init</c> accessors and records
    /// on Unity's .NET Standard 2.1 profile. Newer runtimes ship the type natively.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
