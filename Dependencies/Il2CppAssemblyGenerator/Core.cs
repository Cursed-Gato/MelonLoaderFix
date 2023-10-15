using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using MelonLoader.Il2CppAssemblyGenerator.Packages;
using MelonLoader.Modules;
using MelonLoader.Utils;

namespace MelonLoader.Il2CppAssemblyGenerator
{
    internal class Core : MelonModule
    {
        internal static string BasePath = null;
        internal static string GameAssemblyPath = null;
        internal static string FixFolder = null;
        internal static string ManagedPath = null;

        internal static HttpClient webClient = null;

        internal static Packages.Models.ExecutablePackage dumper = null;
        internal static Packages.Il2CppInterop il2cppinterop = null;
        internal static UnityDependencies unitydependencies = null;
        internal static DeobfuscationMap deobfuscationMap = null;
        internal static DeobfuscationRegex deobfuscationRegex = null;

        internal static bool AssemblyGenerationNeeded = false;

        internal static MelonLogger.Instance Logger;

        public override void OnInitialize()
        {
            Logger = LoggerInstance;

            webClient = new();
            webClient.DefaultRequestHeaders.Add("User-Agent", $"{BuildInfo.Name} v{BuildInfo.Version}");

            AssemblyGenerationNeeded = MelonLaunchOptions.Il2CppAssemblyGenerator.ForceRegeneration;

            string gameAssemblyName = "GameAssembly";
            
            if (MelonUtils.IsUnix)
                gameAssemblyName += ".so"; 
            if (MelonUtils.IsWindows)
                gameAssemblyName += ".dll";
            if (MelonUtils.IsMac)
                gameAssemblyName += ".dylib";

            GameAssemblyPath = Path.Combine(MelonEnvironment.GameRootDirectory,  gameAssemblyName);
            ManagedPath = MelonEnvironment.MelonManagedDirectory;
            FixFolder = Path.Combine(MelonEnvironment.GameRootDirectory, "Fix");

            BasePath = Path.GetDirectoryName(Assembly.Location);
        }

        private static int Run()
        {
            CopyDataCpp2IL();
            Config.Initialize();

            if (!MelonLaunchOptions.Il2CppAssemblyGenerator.OfflineMode)
                RemoteAPI.Contact();

            dumper = new Cpp2IL();
            il2cppinterop = new Packages.Il2CppInterop();
            unitydependencies = new UnityDependencies();
            deobfuscationMap = new DeobfuscationMap();
            deobfuscationRegex = new DeobfuscationRegex();

            Logger.Msg($"Using Dumper Version: {(string.IsNullOrEmpty(dumper.Version) ? "null" : dumper.Version)}");
            Logger.Msg($"Using Il2CppInterop Version = {(string.IsNullOrEmpty(il2cppinterop.Version) ? "null" : il2cppinterop.Version)}");
            Logger.Msg($"Using Unity Dependencies Version = {(string.IsNullOrEmpty(unitydependencies.Version) ? "null" : unitydependencies.Version)}");
            Logger.Msg($"Using Deobfuscation Regex = {(string.IsNullOrEmpty(deobfuscationRegex.Regex) ? "null" : deobfuscationRegex.Regex)}");

            if (!dumper.Setup()
                || !il2cppinterop.Setup()
                || !unitydependencies.Setup()
                || !deobfuscationMap.Setup())
                return 1;

            deobfuscationRegex.Setup();

            string CurrentGameAssemblyHash;
            Logger.Msg("Checking GameAssembly...");
            MelonDebug.Msg($"Last GameAssembly Hash: {Config.Values.GameAssemblyHash}");
            MelonDebug.Msg($"Current GameAssembly Hash: {CurrentGameAssemblyHash = FileHandler.Hash(GameAssemblyPath)}");

            if (string.IsNullOrEmpty(Config.Values.GameAssemblyHash)
                    || !Config.Values.GameAssemblyHash.Equals(CurrentGameAssemblyHash))
                AssemblyGenerationNeeded = true;

            if (!AssemblyGenerationNeeded)
            {
                Logger.Msg("Assembly is up to date. No Generation Needed.");
                return 0;
            }
            Logger.Msg("Assembly Generation Needed!");

            dumper.Cleanup();
            il2cppinterop.Cleanup();

            if (!dumper.Execute())
            {
                dumper.Cleanup();
                return 1;
            }

            if (!il2cppinterop.Execute())
            {
                dumper.Cleanup();
                il2cppinterop.Cleanup();
                return 1;
            }

            OldFiles_Cleanup();
            OldFiles_LAM();

            dumper.Cleanup();
            il2cppinterop.Cleanup();

            Logger.Msg("Assembly Generation Successful!");
            deobfuscationRegex.Save();
            Config.Values.GameAssemblyHash = CurrentGameAssemblyHash;
            Config.Save();

            return 0;
        }

        private static void OldFiles_Cleanup()
        {
            if (Config.Values.OldFiles.Count <= 0)
                return;
            for (int i = 0; i < Config.Values.OldFiles.Count; i++)
            {
                string filename = Config.Values.OldFiles[i];
                string filepath = Path.Combine(MelonEnvironment.Il2CppAssembliesDirectory, filename);
                if (File.Exists(filepath))
                {
                    Logger.Msg("Deleting " + filename);
                    File.Delete(filepath);
                }
            }
            Config.Values.OldFiles.Clear();
        }

        private static void OldFiles_LAM()
        {
            string[] filepathtbl = Directory.GetFiles(il2cppinterop.OutputFolder);
            string il2CppAssembliesDirectory = MelonEnvironment.Il2CppAssembliesDirectory;
            for (int i = 0; i < filepathtbl.Length; i++)
            {
                string filepath = filepathtbl[i];
                string filename = Path.GetFileName(filepath);
                Logger.Msg("Moving " + filename);
                Config.Values.OldFiles.Add(filename);
                string newfilepath = Path.Combine(il2CppAssembliesDirectory, filename);
                if (File.Exists(newfilepath))
                    File.Delete(newfilepath);
                Directory.CreateDirectory(il2CppAssembliesDirectory);
                File.Move(filepath, newfilepath);
            }
            Config.Save();
        }

        private static void CopyDataCpp2IL()
        {
            Logger.Msg("Copying files for Assembly Generation");
            string sourceDirectory = Path.GetDirectoryName(Core.GameAssemblyPath) + @"\RotMG Exalt_Data\";
            string destinationDirectory = Path.GetDirectoryName(Core.FixFolder) + @"\Fix\RotMG Exalt_Data\"; // Replace with the path to your destination folder
            string fixedMetadata = Path.GetDirectoryName(Core.FixFolder) + @"\Fix\fixed-global-metadata.dat";
            string pathGameAssemblyR = Path.GetDirectoryName(Core.FixFolder) + @"\GameAssembly.dll";

            try
            {
                // Check if GameAssembly exists
                if (File.Exists(pathGameAssemblyR))
                {
                    File.Copy(pathGameAssemblyR, Path.GetDirectoryName(Core.FixFolder) + @"\Fix\GameAssembly.dll", true); // The "true" parameter allows overwriting if the file already exists in the destination folder.
                    Logger.Msg("GameAssembly copied successfully.");
                }
                else
                {
                    Logger.Msg("GameAssembly.dll does not exist.");
                }
            }
            catch (Exception ex)
            {
                Logger.Msg($"An error occurred: {ex.Message}");
            }

            try
            {
                string pathToExeR = Path.GetDirectoryName(Core.FixFolder) + @"\" + Process.GetCurrentProcess().ProcessName + ".exe";
                // Check if the source file exists
                if (File.Exists(pathToExeR))
                {
                    File.Copy(pathToExeR, Path.GetDirectoryName(Core.FixFolder) + @"\Fix\" + Process.GetCurrentProcess().ProcessName + ".exe", true); // The "true" parameter allows overwriting if the file already exists in the destination folder.
                    Logger.Msg(Process.GetCurrentProcess().ProcessName + " copied successfully.");
                }
                else
                {
                    Logger.Msg(Process.GetCurrentProcess().ProcessName + " does not exist.");
                }
            }
            catch (Exception ex)
            {
                Logger.Msg($"An error occurred: {ex.Message}");
            }


            // Check if the source directory exists
            if (Directory.Exists(sourceDirectory))
            {
                // Create the destination directory if it doesn't exist
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                // Get a list of all files and subdirectories in the source directory
                string[] files = Directory.GetFiles(sourceDirectory);
                string[] subdirectories = Directory.GetDirectories(sourceDirectory);

                // Copy files
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(destinationDirectory, fileName);
                    File.Copy(file, destFile, true);
                }

                // Copy subdirectories (recursively)
                foreach (string subdir in subdirectories)
                {
                    string subdirName = Path.GetFileName(subdir);
                    string destSubdir = Path.Combine(destinationDirectory, subdirName);
                    DirectoryCopy(subdir, destSubdir);
                }

                Logger.Msg("Folder copied successfully.");
            }
            else
            {
                Logger.Msg("Source directory does not exist.");
            }

            //finnaly copy the fixed-global
            try
            {
                // Check if the source file exists
                if (File.Exists(fixedMetadata))
                {
                    File.Copy(fixedMetadata, Path.GetDirectoryName(Core.FixFolder) + @"\Fix\" + Process.GetCurrentProcess().ProcessName  + @"_Data\il2cpp_data\Metadata\global-metadata.dat", true); // The "true" parameter allows overwriting if the file already exists in the destination folder.
                    Logger.Msg("fixed-global-metadata.dat Moved Successfully.");
                }
                else
                {
                    Logger.Msg("fixed-global-metadata.dat does not exist, if ran already this is fine");
                }
            }
            catch (Exception ex)
            {
                Logger.Msg($"An error occurred: {ex.Message}");
            }

        }

        // Recursive function to copy subdirectories
        public static void DirectoryCopy(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            string[] files = Directory.GetFiles(sourceDir);
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, true);
            }

            string[] subdirectories = Directory.GetDirectories(sourceDir);
            foreach (string subdir in subdirectories)
            {
                string subdirName = Path.GetFileName(subdir);
                string destSubdir = Path.Combine(destDir, subdirName);
                DirectoryCopy(subdir, destSubdir);
            }
        }
    }
}