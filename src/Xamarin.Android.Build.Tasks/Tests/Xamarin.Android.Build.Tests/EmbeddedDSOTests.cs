using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.XPath;

using Microsoft.Build.Framework;
using NUnit.Framework;
using Xamarin.Android.Build.Tests;
using Xamarin.ProjectTools;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Build.Tests
{
	[Category ("StaticProject")] // TODO: enable for .NET 5
	[Parallelizable (ParallelScope.Children)]
	public class EmbeddedDSOTests : BaseTest
	{
		sealed class LocalBuilder : ProjectBuilder
		{
			public LocalBuilder (string projectDir) : base (projectDir)
			{
				BuildingInsideVisualStudio = false;
			}

			public bool Build (string projectOrSolution, string target, string [] parameters = null, Dictionary<string, string> environmentVariables = null)
			{
				return BuildInternal (projectOrSolution, target, parameters, environmentVariables);
			}
		}

		const string ProjectName = "EmbeddedDSO";
		const string ProjectAssemblyName = "Xamarin.Android.EmbeddedDSO_Test";

		static readonly string TestProjectRootDirectory;
		static readonly string TestOutputDir;

		static readonly List <string> produced_binaries = new List <string> {
			$"{ProjectAssemblyName}.dll",
			$"{ProjectAssemblyName}-Signed.apk",
			$"{ProjectAssemblyName}.apk",
		};

		static readonly List <string> log_files = new List <string> {
			"process.log",
			"msbuild.binlog",
		};

		string testProjectPath;
		string androidSdkDir;

		static EmbeddedDSOTests ()
		{
			TestProjectRootDirectory = Path.GetFullPath (Path.Combine (XABuildPaths.TopDirectory, "tests", "EmbeddedDSOs", "EmbeddedDSO"));
			TestOutputDir = Path.Combine (SetUp.TestDirectoryRoot, "temp", "EmbeddedDSO");

			produced_binaries = new List <string> {
				$"{ProjectAssemblyName}.dll",
				$"{ProjectAssemblyName}-Signed.apk",
				$"{ProjectAssemblyName}.apk",
			};
		}

		[OneTimeSetUp]
		public void BuildProject ()
		{
			testProjectPath = PrepareProject (ProjectName);
			string projectPath = Path.Combine (testProjectPath, $"{ProjectName}.csproj");
			LocalBuilder builder = GetBuilder ("EmbeddedDSO", testProjectPath);
			string targetAbis = Xamarin.Android.Tools.XABuildConfig.SupportedABIs.Replace (";", ":");
			bool success = builder.Build (projectPath, "SignAndroidPackage", new [] {
					"UnitTestsMode=true",
					$"Configuration={XABuildPaths.Configuration}",
					$"AndroidSupportedTargetJitAbis=\"{targetAbis}\"",
				});

			Assert.That (success, Is.True, "Should have been built");

			androidSdkDir = AndroidSdkResolver.GetAndroidSdkPath ();
		}

		[OneTimeTearDown]
		public void CleanUp ()
		{
			if (TestContext.CurrentContext.Result.FailCount == 0) {
				FileSystemUtils.SetDirectoryWriteable (TestOutputDir);
				Directory.Delete (TestOutputDir, recursive: true);
			}
		}

		[Test]
		public void BinariesExist ()
		{
			foreach (string binary in produced_binaries) {
				string fp = Path.Combine (testProjectPath, "bin", XABuildPaths.Configuration, binary);

				Assert.That (new FileInfo (fp), Does.Exist, $"File {fp} should exist");
			}
		}

		[Test]
		public void DSOPageAlignment ()
		{
			var zipAlignPath = Path.Combine (GetPathToZipAlign (), IsWindows ? "zipalign.exe" : "zipalign");
			Assert.That (new FileInfo (zipAlignPath), Does.Exist, $"ZipAlign not found at {zipAlignPath}");

			string apk = Path.Combine (testProjectPath, "bin", XABuildPaths.Configuration, $"{ProjectAssemblyName}-Signed.apk");
			Assert.That (new FileInfo (apk), Does.Exist, $"File {apk} should exist");
			Assert.That (RunCommand (zipAlignPath, $"-c -v -p 4 {apk}"), Is.True, $"{ProjectAssemblyName}-Signed.apk does not contain page-aligned .so files");
		}

		[Test]
		public void EnvironmentFileContents ()
		{
			string intermediateOutputDir = Path.Combine (testProjectPath, "obj", XABuildPaths.Configuration);
			List<EnvironmentHelper.EnvironmentFile> envFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediateOutputDir, Xamarin.Android.Tools.XABuildConfig.SupportedABIs, true);
			EnvironmentHelper.ApplicationConfig app_config = EnvironmentHelper.ReadApplicationConfig (envFiles);
			Assert.That (app_config, Is.Not.Null, "application_config must be present in the environment files");
		}

		[Test]
		public void DSOCompressionMode ()
		{
			string apk = Path.Combine (testProjectPath, "bin", XABuildPaths.Configuration, $"{ProjectAssemblyName}-Signed.apk");
			Assert.That (new FileInfo (apk), Does.Exist, $"File {apk} should exist");

			var bad_dsos = new List<string> ();
			using (ZipArchive zip = ZipArchive.Open (apk, FileMode.Open)) {
				Assert.That (zip, Is.Not.Null, $"{apk} couldn't be opened as a zip archive");

				foreach (ZipEntry entry in zip) {
					if (!entry.FullName.EndsWith (".so", StringComparison.Ordinal))
						continue;

					if (entry.CompressionMethod == CompressionMethod.Store)
						continue;

					bad_dsos.Add (entry.FullName);
				}
			}

			Assert.That (bad_dsos.Count == 0, Is.True, $"Some DSO entries in {apk} are compressed ({BadDsosString ()})");

			string BadDsosString ()
			{
				return String.Join ("; ", bad_dsos);
			}
		}

		[Test]
		public void AndroidManifestHasFlag ()
		{
			const string AndroidNS = "http://schemas.android.com/apk/res/android";

			string manifest = Path.Combine (testProjectPath, "obj", XABuildPaths.Configuration, "android", "manifest", "AndroidManifest.xml");
			Assert.That (new FileInfo (manifest), Does.Exist, $"File {manifest} should exist");

			using (var reader = XmlReader.Create (manifest, new XmlReaderSettings { XmlResolver = null })) {
				var doc = new XPathDocument (reader);
				XPathNavigator nav = doc.CreateNavigator ();

				var manager = new XmlNamespaceManager (nav.NameTable);
				manager.AddNamespace ("android", AndroidNS);

				XPathNavigator application = nav.SelectSingleNode ("//manifest/application");
				Assert.That (application, Is.Not.Null, $"Manifest {manifest} does not contain the `application` node");

				string attr = application.GetAttribute ("extractNativeLibs", AndroidNS)?.Trim ();
				Assert.That (String.IsNullOrEmpty (attr), Is.False, $"Manifest {manifest} `application` node does not contain the `extractNativeLibs` attribute");
				Assert.That (String.Compare ("false", attr, StringComparison.OrdinalIgnoreCase), Is.EqualTo (0), $"Manifest {manifest} `application` node's `extractNativeLibs` attribute is not set to `false`");
			}
		}

		bool RunCommand (string command, string arguments)
		{
			var psi = new ProcessStartInfo () {
				FileName		= command,
				Arguments		= arguments,
				UseShellExecute		= false,
				RedirectStandardInput	= false,
				RedirectStandardOutput	= true,
				RedirectStandardError	= true,
				CreateNoWindow		= true,
				WindowStyle		= ProcessWindowStyle.Hidden,
			};

			var stderr_completed = new ManualResetEvent (false);
			var stdout_completed = new ManualResetEvent (false);

			var p = new Process () {
				StartInfo   = psi,
			};

			p.ErrorDataReceived += (sender, e) => {
				if (e.Data == null)
					stderr_completed.Set ();
				else
					Console.WriteLine (e.Data);
			};

			p.OutputDataReceived += (sender, e) => {
				if (e.Data == null)
					stdout_completed.Set ();
				else
					Console.WriteLine (e.Data);
			};

			using (p) {
				p.StartInfo = psi;
				p.Start ();
				p.BeginOutputReadLine ();
				p.BeginErrorReadLine ();

				bool success = p.WaitForExit (60000);

				// We need to call the parameter-less WaitForExit only if any of the standard
				// streams have been redirected (see
				// https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=netframework-4.7.2#System_Diagnostics_Process_WaitForExit)
				//
				p.WaitForExit ();
				stderr_completed.WaitOne (TimeSpan.FromSeconds (60));
				stdout_completed.WaitOne (TimeSpan.FromSeconds (60));

				if (!success || p.ExitCode != 0) {
					Console.Error.WriteLine ($"Process `{command} {arguments}` exited with value {p.ExitCode}.");
					return false;
				}

				return true;
			}
		}

		string PrepareProject (string testName)
		{
			string tempRoot = Path.Combine (TestOutputDir, $"{testName}.build", XABuildPaths.Configuration);
			string temporaryProjectPath = Path.Combine (tempRoot, "project");

			var ignore = new HashSet <string> {
				Path.Combine (TestProjectRootDirectory, "bin"),
				Path.Combine (TestProjectRootDirectory, "obj"),
			};

			CopyRecursively (TestProjectRootDirectory, temporaryProjectPath, ignore);
			return temporaryProjectPath;
		}

		void CopyRecursively (string fromDir, string toDir, HashSet <string> ignoreDirs)
		{
			if (String.IsNullOrEmpty (fromDir))
				throw new ArgumentException ($"{nameof (fromDir)} is must have a non-empty value");
			if (String.IsNullOrEmpty (toDir))
				throw new ArgumentException ($"{nameof (toDir)} is must have a non-empty value");

			if (ignoreDirs.Contains (fromDir))
				return;

			var fdi = new DirectoryInfo (fromDir);
			if (!fdi.Exists)
				throw new InvalidOperationException ($"Source directory '{fromDir}' does not exist");

			if (Directory.Exists (toDir))
				Directory.Delete (toDir, true);

			foreach (FileSystemInfo fsi in fdi.EnumerateFileSystemInfos ("*", SearchOption.TopDirectoryOnly)) {
				if (fsi is FileInfo finfo)
					CopyFile (fsi.FullName, Path.Combine (toDir, finfo.Name));
				else
					CopyRecursively (fsi.FullName, Path.Combine (toDir, fsi.Name), ignoreDirs);
			}
		}

		void CopyFile (string from, string to)
		{
			string dir = Path.GetDirectoryName (to);
			if (!Directory.Exists (dir))
				Directory.CreateDirectory (dir);
			File.Copy (from, to, true);
		}

		LocalBuilder GetBuilder (string baseLogFileName, string projectDir)
		{
			return new LocalBuilder (projectDir) {
				BuildLogFile = $"{baseLogFileName}.log"
			};
		}
	}
}
