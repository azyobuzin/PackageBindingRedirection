using System;

namespace PackageBindingRedirection
{
    internal class AssemblyNameInfo
    {
        public string Name { get; }
        public Version Version { get; }
        public byte[] PublicKeyToken { get; }
        public string Culture { get; }

        public AssemblyNameInfo(string name, Version version, byte[] publicKeyToken, string culture)
        {
            this.Name = name;
            this.Version = version;
            this.PublicKeyToken = publicKeyToken;
            this.Culture = culture;
        }
    }
}
