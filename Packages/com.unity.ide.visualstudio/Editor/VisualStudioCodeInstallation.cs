﻿/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using IOPath = System.IO.Path;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Unity.VisualStudio.Editor
{
	internal class VisualStudioCodeInstallation : VisualStudioInstallation
	{
		private static readonly IGenerator _generator = new SdkStyleProjectGeneration();

		public override bool SupportsAnalyzers
		{
			get
			{
				return true;
			}
		}

		public override Version LatestLanguageVersionSupported
		{
			get
			{
				return new Version(11, 0);
			}
		}

		private string GetExtensionPath()
		{
			var vscode = IsPrerelease ? ".vscode-insiders" : ".vscode";
			var extensionsPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), vscode, "extensions");
			if (!Directory.Exists(extensionsPath))
				return null;

			return Directory
				.EnumerateDirectories(extensionsPath, $"{MicrosoftUnityExtensionId}*") // publisherid.extensionid
				.OrderByDescending(n => n)
				.FirstOrDefault();
		}

		public override string[] GetAnalyzers()
		{
			var vstuPath = GetExtensionPath();
			if (string.IsNullOrEmpty(vstuPath))
				return Array.Empty<string>();

			return GetAnalyzers(vstuPath); }

		public override IGenerator ProjectGenerator
		{
			get
			{
				return _generator;
			}
		}

		private static bool IsCandidateForDiscovery(string path)
		{
			if (VisualStudioEditor.IsOSX)
				return Directory.Exists(path) && Regex.IsMatch(path, ".*Code.*.app$", RegexOptions.IgnoreCase);

			if (VisualStudioEditor.IsWindows)
				return File.Exists(path) && Regex.IsMatch(path, ".*Code.*.exe$", RegexOptions.IgnoreCase);

			return File.Exists(path) && path.EndsWith("code", StringComparison.OrdinalIgnoreCase);
		}

		[Serializable]
		internal class VisualStudioCodeManifest
		{
			public string name;
			public string version;
		}

		public static bool TryDiscoverInstallation(string editorPath, out IVisualStudioInstallation installation)
		{
			installation = null;

			if (string.IsNullOrEmpty(editorPath))
				return false;

			if (!IsCandidateForDiscovery(editorPath))
				return false;

			Version version = null;
			var isPrerelease = false;

			try
			{
				var manifestBase = GetRealPath(editorPath);

				if (VisualStudioEditor.IsWindows)  // on Windows, editorPath is a file, resources as subdirectory
					manifestBase = IOPath.GetDirectoryName(manifestBase);
				else if (VisualStudioEditor.IsOSX) // on Mac, editorPath is a directory
					manifestBase = IOPath.Combine(manifestBase, "Contents");
				else                               // on Linux, editorPath is a file, in a bin sub-directory
					manifestBase = Directory.GetParent(manifestBase)?.Parent?.FullName;

				if (manifestBase == null)
					return false;

				var manifestFullPath = IOPath.Combine(manifestBase, @"resources", "app", "package.json");
				if (File.Exists(manifestFullPath))
				{
					var manifest = JsonUtility.FromJson<VisualStudioCodeManifest>(File.ReadAllText(manifestFullPath));
					Version.TryParse(manifest.version.Split('-').First(), out version);
					isPrerelease = manifest.version.ToLower().Contains("insider");
				}
			}
			catch (Exception)
			{
				// do not fail if we are not able to retrieve the exact version number
			}

			isPrerelease = isPrerelease || editorPath.ToLower().Contains("insider");
			installation = new VisualStudioCodeInstallation()
			{
				IsPrerelease = isPrerelease,
				Name = "Visual Studio Code" + (isPrerelease ? " - Insider" : string.Empty) + (version != null ? $" [{version.ToString(3)}]" : string.Empty),
				Path = editorPath,
				Version = version ?? new Version()
			};

			return true;
		}

		public static IEnumerable<IVisualStudioInstallation> GetVisualStudioInstallations()
		{
			var candidates = new List<string>();

			if (VisualStudioEditor.IsWindows)
			{
				var localAppPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
				var programFiles = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

				foreach (var basePath in new[] {localAppPath, programFiles})
				{
					candidates.Add(IOPath.Combine(basePath, "Microsoft VS Code", "Code.exe"));
					candidates.Add(IOPath.Combine(basePath, "Microsoft VS Code Insiders", "Code - Insiders.exe"));
				}
			}
			else if (VisualStudioEditor.IsOSX)
			{
				var appPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
				candidates.AddRange(Directory.EnumerateDirectories(appPath, "Visual Studio Code*.app"));
			}
			else
			{
				candidates.Add("/usr/bin/code");
				candidates.Add("/bin/code");
				candidates.Add("/usr/local/bin/code");
			}

			foreach (var candidate in candidates)
			{
				if (TryDiscoverInstallation(candidate, out var installation))
					yield return installation;
			}
		}

#if UNITY_EDITOR_LINUX
		[DllImport ("libc")]
		private static extern int readlink(string path, byte[] buffer, int buflen);

		internal static string GetRealPath(string path)
		{
			byte[] buf = new byte[512];
			int ret = readlink(path, buf, buf.Length);
			if (ret == -1) return path;
			char[] cbuf = new char[512];
			int chars = System.Text.Encoding.Default.GetChars(buf, 0, ret, cbuf, 0);
			return new String(cbuf, 0, chars);
		}
#else
		internal static string GetRealPath(string path)
		{
			return path;
		}
#endif

		public override void CreateExtraFiles(string projectDirectory)
		{
			try
			{
				var vscodeDirectory = IOPath.Combine(projectDirectory.NormalizePathSeparators(), ".vscode");
				Directory.CreateDirectory(vscodeDirectory);

				var enablePatch = !File.Exists(IOPath.Combine(vscodeDirectory, ".vstupatchdisable"));

				CreateRecommendedExtensionsFile(vscodeDirectory, enablePatch);
				CreateSettingsFile(vscodeDirectory, enablePatch);
				CreateLaunchFile(vscodeDirectory, enablePatch);
			}
			catch (IOException)
			{
			}			
		}

		private const string DefaultLaunchFileContent = @"{
    ""version"": ""0.2.0"",
    ""configurations"": [
        {
            ""name"": ""Attach to Unity"",
            ""type"": ""vstuc"",
            ""request"": ""attach""
        }
     ]
}";

		private static void CreateLaunchFile(string vscodeDirectory, bool enablePatch)
		{
			var launchFile = IOPath.Combine(vscodeDirectory, "launch.json");
			if (File.Exists(launchFile))
			{
				if (enablePatch)
					PatchLaunchFile(launchFile);

				return;
			}

			File.WriteAllText(launchFile, DefaultLaunchFileContent);
		}

		private static void PatchLaunchFile(string launchFile)
		{
			try
			{
				const string configurationsKey = "configurations";
				const string typeKey = "type";

				var content = File.ReadAllText(launchFile);
				var launch = JObject.Parse(content);

				var configurations = (JArray)launch[configurationsKey];
				if (configurations == null)
				{
					configurations = new JArray();
					launch.Add(configurationsKey, configurations);
				}

				var containsVstucEntry = false;
				var patched = false;

				foreach (var entry in configurations.ToArray())
				{
					var type = entry[typeKey].Value<string>();
					
					switch (type)
					{
						case "unity":
							entry.Remove();
							patched = true;
							break;

						case "vstuc":
							containsVstucEntry = true;
							break;
					}
				}

				if (!containsVstucEntry)
				{
					var defaultContent = JObject.Parse(DefaultLaunchFileContent);
					configurations.Add(defaultContent[configurationsKey].First());
					patched = true;
				}

				if (patched)
					WriteAllTextFromJObject(launchFile, launch);
			}
			catch (Exception)
			{
				// do not fail if we cannot patch the launch.json file
			}
		}

		private void CreateSettingsFile(string vscodeDirectory, bool enablePatch)
		{
			var settingsFile = IOPath.Combine(vscodeDirectory, "settings.json");
			if (File.Exists(settingsFile))
			{
				if (enablePatch)
					PatchSettingsFile(settingsFile);

				return;
			}

			const string excludes = @"    ""files.exclude"": {
        ""**/.DS_Store"": true,
        ""**/.git"": true,
        ""**/.vs"": true,
        ""**/.gitmodules"": true,
        ""**/.vsconfig"": true,
        ""**/*.booproj"": true,
        ""**/*.pidb"": true,
        ""**/*.suo"": true,
        ""**/*.user"": true,
        ""**/*.userprefs"": true,
        ""**/*.unityproj"": true,
        ""**/*.dll"": true,
        ""**/*.exe"": true,
        ""**/*.pdf"": true,
        ""**/*.mid"": true,
        ""**/*.midi"": true,
        ""**/*.wav"": true,
        ""**/*.gif"": true,
        ""**/*.ico"": true,
        ""**/*.jpg"": true,
        ""**/*.jpeg"": true,
        ""**/*.png"": true,
        ""**/*.psd"": true,
        ""**/*.tga"": true,
        ""**/*.tif"": true,
        ""**/*.tiff"": true,
        ""**/*.3ds"": true,
        ""**/*.3DS"": true,
        ""**/*.fbx"": true,
        ""**/*.FBX"": true,
        ""**/*.lxo"": true,
        ""**/*.LXO"": true,
        ""**/*.ma"": true,
        ""**/*.MA"": true,
        ""**/*.obj"": true,
        ""**/*.OBJ"": true,
        ""**/*.asset"": true,
        ""**/*.cubemap"": true,
        ""**/*.flare"": true,
        ""**/*.mat"": true,
        ""**/*.meta"": true,
        ""**/*.prefab"": true,
        ""**/*.unity"": true,
        ""build/"": true,
        ""Build/"": true,
        ""Library/"": true,
        ""library/"": true,
        ""obj/"": true,
        ""Obj/"": true,
        ""Logs/"": true,
        ""logs/"": true,
        ""ProjectSettings/"": true,
        ""UserSettings/"": true,
        ""temp/"": true,
        ""Temp/"": true
    }";

			var content = @"{
" + excludes + @",
    ""dotnet.defaultSolution"": """ + IOPath.GetFileName(ProjectGenerator.SolutionFile()) + @"""
}";

			File.WriteAllText(settingsFile, content);
		}

		private void PatchSettingsFile(string settingsFile)
		{
			try
			{
				const string excludesKey = "files.exclude";
				const string solutionKey = "dotnet.defaultSolution";

				var content = File.ReadAllText(settingsFile);
				var settings = JObject.Parse(content);

				var excludes = (JObject)settings[excludesKey];
				if (excludes == null)
					return;

				var patchList = new List<string>();
				var patched = false;

				// Remove files.exclude for solution+project files in the project root
				foreach (var exclude in excludes)
				{
					if (!exclude.Value.Value<bool>())
						continue;

					if (Regex.IsMatch(exclude.Key, "^(\\*\\*[\\\\\\/])?\\*\\.(sln|csproj)$"))
					{
						patchList.Add(exclude.Key);
						patched = true;
					}
				}

				// Check default solution
				var defaultSolution = settings[solutionKey];
				var solutionFile = IOPath.GetFileName(ProjectGenerator.SolutionFile());
				if (defaultSolution == null || defaultSolution.Value<string>() != solutionFile)
				{
					settings[solutionKey] = solutionFile;
					patched = true;
				}

				if (patched)
				{
					foreach (var patch in patchList)
						excludes.Remove(patch);

					WriteAllTextFromJObject(settingsFile, settings);
				}
			}
			catch (Exception)
			{
				// do not fail if we cannot patch the settings.json file
			}
		}

		private const string MicrosoftUnityExtensionId = "visualstudiotoolsforunity.vstuc";
		private const string DefaultRecommendedExtensionsContent = @"{
    ""recommendations"": [
      """+ MicrosoftUnityExtensionId + @"""
    ]
}
";

		private static void CreateRecommendedExtensionsFile(string vscodeDirectory, bool enablePatch)
		{
			// see https://tattoocoder.com/recommending-vscode-extensions-within-your-open-source-projects/
			var extensionFile = IOPath.Combine(vscodeDirectory, "extensions.json");
			if (File.Exists(extensionFile))
			{
				if (enablePatch)
					PatchRecommendedExtensionsFile(extensionFile);

				return;
			}

			File.WriteAllText(extensionFile, DefaultRecommendedExtensionsContent);
		}

		private static void PatchRecommendedExtensionsFile(string extensionFile)
		{
			try
			{
				const string recommendationsKey = "recommendations";

				var content = File.ReadAllText(extensionFile);
				var extensions = JObject.Parse(content);

				var recommendations = (JArray)extensions[recommendationsKey];
				if (recommendations == null)
				{
					recommendations = new JArray();
					extensions.Add(recommendationsKey, recommendations);
				}

				foreach(var entry in recommendations)
				{
					if (entry.Value<string>() == MicrosoftUnityExtensionId)
						return;
				}

				recommendations.Add(MicrosoftUnityExtensionId);
				WriteAllTextFromJObject(extensionFile, extensions);
			}
			catch (Exception)
			{
				// do not fail if we cannot patch the extensions.json file
			}
		}

		private static void WriteAllTextFromJObject(string file, JObject jobject)
		{
			using (var fs = File.Open(file, FileMode.Create))
			using (var sw = new StreamWriter(fs))
			using (var jw = new JsonTextWriter(sw))
			{
				// Keep formatting/indent in sync with default contents
				jw.Formatting = Formatting.Indented;
				jw.IndentChar = ' ';
				jw.Indentation = 4;

				var serializer = new JsonSerializer();
				serializer.Serialize(jw, jobject);
			}
		}

		public override bool Open(string path, int line, int column, string solution)
		{
			line = Math.Max(1, line);
			column = Math.Max(0, column);

			var directory = IOPath.GetDirectoryName(solution);
			var application = Path;

			ProcessRunner.Start(string.IsNullOrEmpty(path) ? 
				ProcessStartInfoFor(application, $"\"{directory}\"") :
				ProcessStartInfoFor(application, $"\"{directory}\" -g \"{path}\":{line}:{column}"));

			return true;
		}

		private static ProcessStartInfo ProcessStartInfoFor(string application, string arguments)
		{
			if (!VisualStudioEditor.IsOSX)
				return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect: false);

			// wrap with built-in OSX open feature
			arguments = $"-n \"{application}\" --args {arguments}";
			application = "open";
			return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect:false, shell: true);
		}

		public static void Initialize()
		{
		}
	}
}
