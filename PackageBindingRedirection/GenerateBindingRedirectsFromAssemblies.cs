using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;

namespace PackageBindingRedirection
{
    public class GenerateBindingRedirectsFromAssemblies : Task
    {
        private static readonly XNamespace assemblyBindingNamespace = "urn:schemas-microsoft-com:asm.v1";

        public ITaskItem AppConfigFile { get; set; }

        public ITaskItem[] InputFiles { get; set; }

        public string[] Exclusions { get; set; }

        [Required, Output]
        public ITaskItem OutputAppConfigFile { get; set; }

        private XDocument _document;
        private XElement _assemblyBindingElement;
        private readonly HashSet<string> _excludedAssemblyNames = new HashSet<string>();

        public override bool Execute()
        {
            if (this.Exclusions != null)
            {
                foreach (var x in this.Exclusions)
                    this._excludedAssemblyNames.Add(x);
            }

            this.LoadAppConfig();

            var assemblies = new SortedDictionary<string, AssemblyNameInfo>();
            foreach (var x in this.EnumerateAssemblyNameInfo())
            {
                if (!this._excludedAssemblyNames.Contains(x.Name) &&
                    (!assemblies.TryGetValue(x.Name, out var y) || x.Version > y.Version))
                    assemblies[x.Name] = x;
            }

            this.WriteOutput(assemblies.Values);

            if (this.AppConfigFile != null)
                this.AppConfigFile.CopyMetadataTo(this.OutputAppConfigFile);

            return true;
        }

        private void LoadAppConfig()
        {
            var appConfigPath = this.AppConfigFile?.GetMetadata("FullPath");

            if (!string.IsNullOrEmpty(appConfigPath) && File.Exists(appConfigPath))
            {
                this._document = XDocument.Load(File.OpenRead(appConfigPath));
                var root = this._document.Root;

                var runtimeElement = root.Element("runtime");
                if (runtimeElement == null)
                {
                    runtimeElement = new XElement("runtime");
                    root.Add(runtimeElement);
                }

                var assemblyBindingElement = runtimeElement.Element(assemblyBindingNamespace + "assemblyBinding");
                if (assemblyBindingElement == null)
                {
                    assemblyBindingElement = new XElement(assemblyBindingNamespace + "assemblyBinding");
                    runtimeElement.Add(assemblyBindingElement);
                }
                else
                {
                    var assemblyNames = assemblyBindingElement
                        .Elements(assemblyBindingNamespace + "dependentAssembly")
                        .SelectMany(x => x.Elements(assemblyBindingNamespace + "assemblyIdentity"))
                        .Select(x => (string)x.Attribute("name"));

                    foreach (var x in assemblyNames)
                    {
                        if (!string.IsNullOrEmpty(x))
                            this._excludedAssemblyNames.Add(x);
                    }
                }

                this._assemblyBindingElement = assemblyBindingElement;
            }
            else
            {
                this._assemblyBindingElement = new XElement(assemblyBindingNamespace + "assemblyBinding");
                this._document = new XDocument(
                    new XElement("configuration",
                        new XElement("runtime", this._assemblyBindingElement)
                    )
                );
            }
        }

        private IEnumerable<AssemblyNameInfo> EnumerateAssemblyNameInfo()
        {
            foreach (var inputItem in this.InputFiles)
            {
                var fullPath = inputItem.GetMetadata("FullPath");

                if (!string.Equals(Path.GetExtension(fullPath), ".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                AssemblyNameInfo nameInfo = null;

                try
                {
                    using (var asm = AssemblyDefinition.ReadAssembly(fullPath))
                    {
                        var name = asm.Name;
                        if (name.HasPublicKey)
                        {
                            var culture = name.Culture;
                            if (string.IsNullOrEmpty(culture)) culture = "neutral";

                            nameInfo = new AssemblyNameInfo(name.Name, name.Version, name.PublicKeyToken, culture);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Log.LogMessage(MessageImportance.Low, "Failed to read {0}: {1}", fullPath, ex);
                }

                if (nameInfo != null) yield return nameInfo;
            }
        }

        private void WriteOutput(IEnumerable<AssemblyNameInfo> assemblyNameInfo)
        {
            foreach (var x in assemblyNameInfo)
            {
                var version = x.Version.ToString();
                this._assemblyBindingElement.Add(new XElement(
                    assemblyBindingNamespace + "dependentAssembly",
                    new XElement(
                        assemblyBindingNamespace + "assemblyIdentity",
                        new XAttribute("name", x.Name),
                        new XAttribute("publicKeyToken", string.Concat(x.PublicKeyToken.Select(y => y.ToString("x2")))),
                        new XAttribute("culture", x.Culture)
                    ),
                    new XElement(
                        assemblyBindingNamespace + "bindingRedirect",
                        new XAttribute("oldVersion", "0.0.0.0-" + version),
                        new XAttribute("newVersion", version)
                    )
                ));
            }

            using (var stream = File.OpenWrite(this.OutputAppConfigFile.GetMetadata("FullPath")))
                this._document.Save(stream);
        }
    }
}
