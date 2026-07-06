// Compiler polyfills so C# 'init' and 'required' members compile on netstandard2.1.
// These types are recognized by name by the compiler; they carry no runtime behavior.
#if NETSTANDARD2_1

namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    /// <summary>Enables C# <c>init</c> property setters.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }

    /// <summary>Enables C# <c>required</c> members.</summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    /// <summary>Required by the compiler when emitting <c>required</c> members.</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;

        public string FeatureName { get; }
        public bool IsOptional { get; init; }

        public const string RefStructs      = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>Marks a constructor as setting all <c>required</c> members.</summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}

#endif
