namespace Nancy.Bootstrapper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    using Nancy.Extensions;

    /// <summary>
    /// Scans the app domain for assemblies and types
    /// </summary>
    public static class AppDomainAssemblyTypeScanner
    {
        static AppDomainAssemblyTypeScanner()
        {
            SetDefaultAssembliesToScan();

            LoadNancyAssemblies();
        }

        /// <summary>
        /// Nancy core assembly
        /// </summary>
        private static Assembly nancyAssembly = typeof(NancyEngine).Assembly;

        /// <summary>
        /// App domain type cache
        /// </summary>
        private static IEnumerable<Type> types;

        /// <summary>
        /// App domain assemblies cache
        /// </summary>
        private static IEnumerable<Assembly> assemblies;

        /// <summary>
        /// Indicates whether the nancy assemblies have already been loaded
        /// </summary>
        private static bool nancyAssembliesLoaded;

        private static IEnumerable<Func<Assembly, bool>> assembliesToScan;

        /// <summary>
        /// Gets or sets a set of rules for which assemblies are scanned
        /// Defaults to just assemblies that have references to nancy, and nancy
        /// itself.
        /// Each item in the enumerable is a delegate that takes the assembly and 
        /// returns true if it is to be included. Returning false doesn't mean it won't
        /// be included as a true from another delegate will take precedence.
        /// </summary>
        public static IEnumerable<Func<Assembly, bool>> AssembliesToScan
        { 
            private get 
            {
                return assembliesToScan;
            } 
            set 
            {
                assembliesToScan = value;
                UpdateTypes ();
            }
        }

        /// <summary>
        /// Gets app domain types.
        /// </summary>
        public static IEnumerable<Type> Types
        {
            get
            {
                return types;
            }
        }

        /// <summary>
        /// Gets app domain types.
        /// </summary>
        public static IEnumerable<Assembly> Assemblies
        {
            get
            {
                return assemblies;
            }
        }

        /// <summary>
        /// Load assemblies from a the app domain base directory matching a given wildcard.
        /// Assemblies will only be loaded if they aren't already in the appdomain.
        /// </summary>
        /// <param name="wildcardFilename">Wildcard to match the assemblies to load</param>
        public static void LoadAssemblies(string wildcardFilename)
        {
            foreach (var directory in GetAssemblyDirectories())
            {
                LoadAssemblies(directory, wildcardFilename);
            }
        }

        /// <summary>
        /// Load assemblies from a given directory matching a given wildcard.
        /// Assemblies will only be loaded if they aren't already in the appdomain.
        /// </summary>
        /// <param name="containingDirectory">Directory containing the assemblies</param>
        /// <param name="wildcardFilename">Wildcard to match the assemblies to load</param>
        public static void LoadAssemblies(string containingDirectory, string wildcardFilename)
        {
            UpdateAssemblies();

            var existingAssemblyPaths = assemblies.Select(a => a.Location).ToArray();

            var unloadedAssemblies =
                Directory.GetFiles(containingDirectory, wildcardFilename).Where(
                    f => !existingAssemblyPaths.Contains(f, StringComparer.InvariantCultureIgnoreCase)).ToArray();


            foreach (var unloadedAssembly in unloadedAssemblies)
            {
                Assembly.Load(AssemblyName.GetAssemblyName(unloadedAssembly));
            }

            UpdateTypes();
        }

        /// <summary>
        /// Refreshes the type cache if additional assemblies have been loaded.
        /// Note: This is called automatically if assemblies are loaded using LoadAssemblies.
        /// </summary>
        public static void UpdateTypes()
        {
            UpdateAssemblies();

            types = (from assembly in assemblies
                     from type in assembly.SafeGetExportedTypes()
                     where !type.IsAbstract
                     select type).ToArray();
        }

        private static void SetDefaultAssembliesToScan()
        {
            assembliesToScan = new Func<Assembly, bool>[]
                                   {
                                       x => x == nancyAssembly,
                                       x => x.GetReferencedAssemblies().Any(r => r.Name.StartsWith("Nancy", StringComparison.OrdinalIgnoreCase))
                                   };
        }

        /// <summary>
        /// Updates the assembly cache from the appdomain
        /// </summary>
        private static void UpdateAssemblies()
        {
            assemblies = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                          where AssembliesToScan.Any(asm => asm(assembly))
                          where !assembly.IsDynamic
                          where !assembly.ReflectionOnly
                          select assembly).ToArray();
        }

        /// <summary>
        /// Loads any Nancy*.dll assemblies in the app domain base directory
        /// </summary>
        public static void LoadNancyAssemblies()
        {
            if (nancyAssembliesLoaded)
            {
                return;
            }

            LoadAssemblies(@"Nancy*.dll");

            nancyAssembliesLoaded = true;
        }

        /// <summary>
        /// Gets all types implementing a particular interface/base class
        /// </summary>
        /// <typeparam name="TType">Type to search for</typeparam>
        /// <param name="excludeInternalTypes">Whether to exclude types inside the core Nancy assembly</param>
        /// <returns>IEnumerable of types</returns>
        [Obsolete("This method has been replaced by the overload that accepts a ScanMode parameter and will be removed in a subsequent release.")]
        public static IEnumerable<Type> TypesOf<TType>(bool excludeInternalTypes = false)
        {
            var returnTypes = Types.Where(t => typeof(TType).IsAssignableFrom(t));

            if (excludeInternalTypes)
            {
                returnTypes = returnTypes.Where(t => t.Assembly != nancyAssembly);
            }

            return returnTypes;
        }

        /// <summary>
        /// Gets all types implementing a particular interface/base class
        /// </summary>
        /// <param name="type">Type to search for</param>
        /// <returns>An <see cref="IEnumerable{T}"/> of types.</returns>
        /// <remarks>Will scan with <see cref="ScanMode.All"/>.</remarks>
        public static IEnumerable<Type> TypesOf(Type type)
        {
            return TypesOf(type, ScanMode.All);
        }

        /// <summary>
        /// Gets all types implementing a particular interface/base class
        /// </summary>
        /// <param name="type">Type to search for</param>
        /// <param name="mode">A <see cref="ScanMode"/> value to determin which type set to scan in.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> of types.</returns>
        public static IEnumerable<Type> TypesOf(Type type, ScanMode mode)
        {
            var returnTypes =
                Types.Where(type.IsAssignableFrom);

            if (mode == ScanMode.All)
            {
                return returnTypes;
            }

            return (mode == ScanMode.OnlyNancy) ?
                returnTypes.Where(t => t.Assembly == nancyAssembly) :
                returnTypes.Where(t => t.Assembly != nancyAssembly);
        }

        /// <summary>
        /// Gets all types implementing a particular interface/base class
        /// </summary>
        /// <typeparam name="TType">Type to search for</typeparam>
        /// <returns>An <see cref="IEnumerable{T}"/> of types.</returns>
        /// <remarks>Will scan with <see cref="ScanMode.All"/>.</remarks>
        public static IEnumerable<Type> TypesOf<TType>()
        {
            return TypesOf<TType>(ScanMode.All);
        }

        /// <summary>
        /// Gets all types implementing a particular interface/base class
        /// </summary>
        /// <typeparam name="TType">Type to search for</typeparam>
        /// <param name="mode">A <see cref="ScanMode"/> value to determin which type set to scan in.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> of types.</returns>
        public static IEnumerable<Type> TypesOf<TType>(ScanMode mode)
        {
            return TypesOf(typeof(TType), mode);
        }

        /// <summary>
        /// Returns the directories containing dll files. It uses the default convention as stated by microsoft.
        /// </summary>
        /// <see cref="http://msdn.microsoft.com/en-us/library/system.appdomainsetup.privatebinpathprobe.aspx"/>
        private static IEnumerable<string> GetAssemblyDirectories()
        {
            var privateBinPathDirectories = AppDomain.CurrentDomain.SetupInformation.PrivateBinPath == null
                                                ? new string[] { }
                                                : AppDomain.CurrentDomain.SetupInformation.PrivateBinPath.Split(';');

            foreach (var privateBinPathDirectory in privateBinPathDirectories)
            {
                if (!string.IsNullOrWhiteSpace(privateBinPathDirectory))
                {
                    yield return privateBinPathDirectory;
                }
            }

            if (AppDomain.CurrentDomain.SetupInformation.PrivateBinPathProbe == null)
            {
                yield return AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            }
        }
    }

    public static class AppDomainAssemblyTypeScannerExtensions
    {
        public static IEnumerable<Type> NotOfType<TType>(this IEnumerable<Type> types)
        {
            return types.Where(t => !typeof(TType).IsAssignableFrom(t));
        }
    }
}
