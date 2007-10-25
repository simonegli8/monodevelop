// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Mike KrÃ¼ger" email="mike@icsharpcode.net"/>
//     <version value="$version"/>
// </file>

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.CodeDom.Compiler;
using System.Threading;

using MonoDevelop.Core;
using MonoDevelop.Core.Execution;
using MonoDevelop.Projects;

namespace CSharpBinding
{
	/// <summary>
	/// This class controls the compilation of C Sharp files and C Sharp projects
	/// </summary>
	public class CSharpBindingCompilerManager
	{	
		public bool CanCompile(string fileName)
		{
			return Path.GetExtension(fileName).ToUpper() == ".CS";
		}

		public ICompilerResult Compile (ProjectFileCollection projectFiles, ProjectReferenceCollection references, DotNetProjectConfiguration configuration, IProgressMonitor monitor)
		{
			CSharpCompilerParameters compilerparameters = (CSharpCompilerParameters) configuration.CompilationParameters;
			if (compilerparameters == null) compilerparameters = new CSharpCompilerParameters ();
			
			string exe = configuration.CompiledOutputName;
			string responseFileName = Path.GetTempFileName();
			StringWriter writer = new StringWriter ();
			bool hasWin32Res = false;
			ArrayList gacRoots = new ArrayList ();

			writer.WriteLine("\"/out:" + exe + '"');
			
			ArrayList pkg_references = new ArrayList ();
			
			if (references != null) {
				foreach (ProjectReference lib in references) {
					foreach (string fileName in lib.GetReferencedFileNames ()) {
						switch (lib.ReferenceType) {
						case ReferenceType.Gac:
							SystemPackage pkg = Runtime.SystemAssemblyService.GetPackageFromFullName (lib.Reference);
							if (pkg == null) {
								string msg = String.Format (GettextCatalog.GetString ("{0} could not be found or is invalid."), lib.Reference);
								monitor.ReportWarning (msg);
								continue;
							}
							if (pkg.IsCorePackage) {
								writer.WriteLine ("\"/r:" + Path.GetFileName (fileName) + "\"");
							} else if (pkg.IsInternalPackage) {
								writer.WriteLine ("\"/r:" + fileName + "\"");
							} else if (!pkg_references.Contains (pkg.Name)) {
								pkg_references.Add (pkg.Name);
								writer.WriteLine ("\"-pkg:" + pkg.Name + "\"");
							}
							if (pkg.GacRoot != null && !gacRoots.Contains (pkg.GacRoot))
								gacRoots.Add (pkg.GacRoot);
							break;
						default:
							writer.WriteLine ("\"/r:" + fileName + "\"");
							break;
						}
					}
				}
			}
			
			writer.WriteLine("/noconfig");
			writer.WriteLine("/nologo");
			writer.WriteLine("/codepage:utf8");
//			writer.WriteLine("/utf8output");
			writer.WriteLine("/warn:" + compilerparameters.WarningLevel);
				
			if (configuration.SignAssembly) {
				if (File.Exists (configuration.AssemblyKeyFile))
					writer.WriteLine("\"/keyfile:" + configuration.AssemblyKeyFile + '"');
			}
			
			if (configuration.DebugMode) {
				writer.WriteLine("/debug:+");
				writer.WriteLine("/debug:full");
				writer.WriteLine("/d:DEBUG");
			}
			
			// mcs default is + but others might not be
			if (compilerparameters.Optimize)
				writer.WriteLine("/optimize+");
			else
				writer.WriteLine("/optimize-");

			if (compilerparameters.Win32Resource != null && compilerparameters.Win32Resource.Length > 0 && File.Exists (compilerparameters.Win32Resource)) {
				writer.WriteLine("\"/win32res:" + compilerparameters.Win32Resource + "\"");
				hasWin32Res = true;
			}
		
			if (compilerparameters.Win32Icon != null && compilerparameters.Win32Icon.Length > 0 && File.Exists (compilerparameters.Win32Icon)) {
				if (hasWin32Res)
					monitor.ReportWarning ("Both Win32 icon and Win32 resource cannot be specified. Ignoring the icon.");
				else
					writer.WriteLine("\"/win32icon:" + compilerparameters.Win32Icon + "\"");
			}
			
			if (compilerparameters.CodePage != 0) {
				writer.WriteLine ("/codepage:" + compilerparameters.CodePage);
			}
			
			if (compilerparameters.UnsafeCode) {
				writer.WriteLine("-unsafe");
			}
			
			if (compilerparameters.DefineSymbols.Length > 0) {
				writer.WriteLine("/define:" + '"' + compilerparameters.DefineSymbols + '"');
			}
			
			if (compilerparameters.MainClass != null && compilerparameters.MainClass.Length > 0) {
				writer.WriteLine("/main:" + compilerparameters.MainClass);
			}
			
			switch (configuration.CompileTarget) {
				case CompileTarget.Exe:
					writer.WriteLine("/t:exe");
					break;
				case CompileTarget.WinExe:
					writer.WriteLine("/t:winexe");
					break;
				case CompileTarget.Library:
					writer.WriteLine("/t:library");
					break;
			}
			
			foreach (ProjectFile finfo in projectFiles) {
				if (finfo.Subtype == Subtype.Directory)
					continue;

				switch (finfo.BuildAction) {
					case BuildAction.Compile:
						writer.WriteLine('"' + finfo.Name + '"');
						break;
					case BuildAction.EmbedAsResource:
						string fname = finfo.Name;
						if (String.Compare (Path.GetExtension (fname), ".resx", true) == 0)
							fname = Path.ChangeExtension (fname, ".resources");

						writer.WriteLine(@"""/res:{0},{1}""", fname, finfo.ResourceId);
						break;
					default:
						continue;
				}
			}
			if (compilerparameters.GenerateXmlDocumentation) {
				writer.WriteLine("\"/doc:" + Path.ChangeExtension(exe, ".xml") + '"');
			}

			writer.Close();

			string output = String.Empty;
			string error  = String.Empty;
			
			string mcs = configuration.ClrVersion == ClrVersion.Net_1_1 ? "mcs" : "gmcs";
			
			File.WriteAllText (responseFileName, writer.ToString ());
			
			string compilerName = compilerparameters.CsharpCompiler == CsharpCompiler.Csc ? GetCompilerName (configuration.ClrVersion) : mcs;
			string outstr = compilerName + " @" + responseFileName;
			TempFileCollection tf = new TempFileCollection();

			string workingDir = ".";
			if (projectFiles != null && projectFiles.Count > 0) {
				workingDir = projectFiles [0].Project.BaseDirectory;
				if (workingDir == null)
					// Dummy projects created for single files have no filename
					// and so no BaseDirectory.
					// This is a workaround for a bug in 
					// ProcessStartInfo.WorkingDirectory - not able to handle null
					workingDir = ".";
			}

			Runtime.LoggingService.Info (compilerName + " " + writer.ToString ());
			
			DoCompilation(outstr, tf, workingDir, gacRoots, ref output, ref error);

			ICompilerResult result = ParseOutput(tf, output, error);
			if (result.CompilerOutput.Trim () != "")
				monitor.Log.WriteLine (result.CompilerOutput);

			FileService.DeleteFile (responseFileName);
			FileService.DeleteFile (output);
			FileService.DeleteFile (error);
			return result;
		}

		string GetCompilerName (ClrVersion version)
		{
			string runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
			// The following regex foo gets the index of the
			// last match of lib/lib32/lib64 and uses
			// the text before that as the 'prefix' in order
			// to find the right mcs to use.
			Regex regex = new Regex ("lib[32 64]?");
			MatchCollection matches = regex.Matches(runtimeDir);
			Match match = matches[matches.Count - 1];
			string mcs = version == ClrVersion.Net_1_1 ? "mcs" : "gmcs";
			string compilerName = Path.Combine (runtimeDir.Substring(0, match.Index), Path.Combine("bin", mcs));
			return compilerName;
		}
		
		ICompilerResult ParseOutput(TempFileCollection tf, string stdout, string stderr)
		{
			StringBuilder compilerOutput = new StringBuilder();
			
			CompilerResults cr = new CompilerResults(tf);
			
			// we have 2 formats for the error output the csc gives :
			//Regex normalError  = new Regex(@"(?<file>.*)\((?<line>\d+),(?<column>\d+)\):\s+(?<error>\w+)\s+(?<number>[\d\w]+):\s+(?<message>.*)", RegexOptions.Compiled);
			//Regex generalError = new Regex(@"(?<error>.+)\s+(?<number>[\d\w]+):\s+(?<message>.*)", RegexOptions.Compiled);
			
			foreach (string s in new string[] { stdout, stderr }) {
				StreamReader sr = File.OpenText (s);
				while (true) {
					string curLine = sr.ReadLine();
					compilerOutput.Append(curLine);
					compilerOutput.Append('\n');
					if (curLine == null) {
						break;
					}
					curLine = curLine.Trim();
					if (curLine.Length == 0) {
						continue;
					}
				
					CompilerError error = CreateErrorFromString (curLine);
					
					if (error != null)
						cr.Errors.Add (error);
				}
				sr.Close();
			}
			return new DefaultCompilerResult(cr, compilerOutput.ToString());
		}
		
		private void DoCompilation(string outstr, TempFileCollection tf, string working_dir, ArrayList gacRoots, ref string output, ref string error) {
			output = Path.GetTempFileName();
			error = Path.GetTempFileName();
			
			StreamWriter outwr = new StreamWriter(output);
			StreamWriter errwr = new StreamWriter(error);
			string[] tokens = outstr.Split(' ');
			
			outstr = outstr.Substring(tokens[0].Length+1);

			ProcessStartInfo pinfo = new ProcessStartInfo (tokens[0], "\"" + outstr + "\"");
			pinfo.WorkingDirectory = working_dir;
			
			if (gacRoots.Count > 0) {
				// Create the gac prefix string
				string gacPrefix = string.Join ("" + Path.PathSeparator, (string[])gacRoots.ToArray (typeof(string)));
				string oldGacVar = Environment.GetEnvironmentVariable ("MONO_GAC_PREFIX");
				if (!string.IsNullOrEmpty (oldGacVar))
					gacPrefix += Path.PathSeparator + oldGacVar;
				pinfo.EnvironmentVariables ["MONO_GAC_PREFIX"] = gacPrefix;
			}
			pinfo.UseShellExecute = false;
			pinfo.RedirectStandardOutput = true;
			pinfo.RedirectStandardError = true;
			
			ProcessWrapper pw = Runtime.ProcessService.StartProcess (pinfo, outwr, errwr, null);
			pw.WaitForOutput();
			outwr.Close();
			errwr.Close();
		}


		// Snatched from our codedom code :-).
		static Regex regexError = new Regex (@"^(\s*(?<file>.*)\((?<line>\d*)(,(?<column>\d*[\+]*))?\)(:|)\s+)*(?<level>\w+)\s*(?<number>.*):\s(?<message>.*)",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);

		private static CompilerError CreateErrorFromString(string error_string)
		{
			// When IncludeDebugInformation is true, prevents the debug symbols stats from braeking this.
			if (error_string.StartsWith ("WROTE SYMFILE") ||
			    error_string.StartsWith ("OffsetTable") ||
			    error_string.StartsWith ("Compilation succeeded") ||
			    error_string.StartsWith ("Compilation failed"))
				return null;

			CompilerError error = new CompilerError();

			Match match=regexError.Match(error_string);
			if (!match.Success) return null;
			if (String.Empty != match.Result("${file}"))
				error.FileName=match.Result("${file}");
			if (String.Empty != match.Result("${line}"))
				error.Line=Int32.Parse(match.Result("${line}"));
			if (String.Empty != match.Result("${column}")) {
				if (match.Result("${column}") == "255+")
					error.Column = -1;
				else
					error.Column=Int32.Parse(match.Result("${column}"));
			}
			if (match.Result("${level}")=="warning")
				error.IsWarning=true;
			error.ErrorNumber=match.Result("${number}");
			error.ErrorText=match.Result("${message}");
			return error;
		}

	}
}
