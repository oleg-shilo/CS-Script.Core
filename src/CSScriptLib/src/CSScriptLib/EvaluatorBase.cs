#region License...

//-----------------------------------------------------------------------------
// Date:	20/12/15	Time: 9:00
// Module:	CSScriptLib.Eval.Roslyn.cs
//
// This module contains the definition of the Roslyn Evaluator class. Which wraps the common functionality
// of the Mono.CScript.Evaluator class (compiler as service)
//
// Written by Oleg Shilo (oshilo@gmail.com)
//----------------------------------------------
// The MIT License (MIT)
// Copyright (c) 2016 Oleg Shilo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software
// and associated documentation files (the "Software"), to deal in the Software without restriction,
// including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial
// portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
// LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//----------------------------------------------

#endregion License...

using csscript;
using CSScripting;
using Microsoft.CodeAnalysis;

//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp.Scripting
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CSScriptLib
{
    /// <summary>
    /// A wrapper class that encapsulates the functionality of the Roslyn  evaluator (<see cref="Microsoft.CodeAnalysis.CSharp.Scripting"/>).
    /// </summary>
    public class EvaluatorBase<T> : IEvaluator where T : IEvaluator, new()
    {
        /// <summary>
        /// Clones itself as <see cref="CSScriptLib.IEvaluator"/>.
        /// <para>
        /// This method returns a freshly initialized copy of the <see cref="CSScriptLib.IEvaluator"/>.
        /// The cloning 'depth' can be controlled by the <paramref name="copyRefAssemblies"/>.
        /// </para>
        /// <para>
        /// This method is a convenient technique when multiple <see cref="CSScriptLib.IEvaluator"/> instances
        /// are required (e.g. for concurrent script evaluation).
        /// </para>
        /// </summary>
        /// <param name="copyRefAssemblies">if set to <c>true</c> all referenced assemblies from the parent <see cref="CSScriptLib.IEvaluator"/>
        /// will be referenced in the cloned copy.</param>
        /// <returns>The freshly initialized instance of the <see cref="CSScriptLib.IEvaluator"/>.</returns>
        public IEvaluator Clone(bool copyRefAssemblies = true)
        {
            var clone = new T();
            if (copyRefAssemblies)
            {
                clone.Reset(false);
                foreach (var a in this.GetReferencedAssemblies())
                    clone.ReferenceAssembly(a);
            }
            return clone;
        }

        static Assembly mscorelib = 333.GetType().Assembly;

        /// <summary>
        /// Gets or sets a value indicating whether to compile script with debug symbols.
        /// <para>Note, setting <c>DebugBuild</c> will only affect the current instance of Evaluator.
        /// If you want to emit debug symbols for all instances of Evaluator then use
        /// <see cref="CSScriptLib.CSScript.EvaluatorConfig"/>.DebugBuild.
        /// </para>
        /// </summary>
        /// <value><c>true</c> if 'debug build'; otherwise, <c>false</c>.</value>
        public bool? DebugBuild { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is debug.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is debug; otherwise, <c>false</c>.
        /// </value>
        protected bool IsDebug => DebugBuild ?? CSScript.EvaluatorConfig.DebugBuild;

        /// <summary>
        /// Initializes a new instance of the <see cref="RoslynEvaluator" /> class.
        /// </summary>
        public EvaluatorBase()
        {
            if (CSScript.EvaluatorConfig.RefernceDomainAsemblies)
                ReferenceDomainAssemblies();
        }

        /// <summary>
        /// Evaluates (compiles) C# code (script). The C# code is a typical C# code containing a single or multiple class definition(s).
        /// </summary>
        /// <example>
        ///<code>
        /// Assembly asm = CSScript.RoslynEvaluator
        ///                        .CompileCode(@"using System;
        ///                                       public class Script
        ///                                       {
        ///                                           public int Sum(int a, int b)
        ///                                           {
        ///                                               return a+b;
        ///                                           }
        ///                                       }");
        ///
        /// dynamic script =  asm.CreateObject("*");
        /// var result = script.Sum(7, 3);
        /// </code>
        /// </example>
        /// <param name="scriptText">The C# script text.</param>
        /// <returns>The compiled assembly.</returns>
        public Assembly CompileCode(string scriptText)
        {
            return CompileCode(scriptText, null, null);
        }

        /// <summary>
        /// Evaluates (compiles) C# code (script). The C# code is a typical C# code containing a single or multiple class definition(s).
        /// <para>The method is identical to <see cref="IEvaluator.CompileCode(string, CompileInfo)"/> except that it allows specifying
        /// the destination assembly file with <see cref="CompileInfo"/> object.</para>
        /// </summary>
        /// <example>
        /// <code>
        /// var info = new CompileInfo
        /// {
        ///     AssemblyFile = @"E:\temp\asm.dll"
        /// };
        ///
        /// Assembly asm = CSScript.Evaluator
        ///                        .Cast&lt;RoslynEvaluator&gt;()
        ///                        .CompileCode(@"using System;
        ///                                       public class Script
        ///                                       {
        ///                                           public int Sum(int a, int b)
        ///                                           {
        ///                                               return a+b;
        ///                                           }
        ///                                       }",
        ///                                       info);
        ///
        /// dynamic script =  asm.CreateObject("*");
        /// var result = script.Sum(7, 3);
        /// </code>
        /// </example>
        /// <param name="scriptText">The C# script text.</param>
        /// <param name="info"></param>
        /// <returns>The compiled assembly.</returns>
        public Assembly CompileCode(string scriptText, CompileInfo info = null)
        {
            return CompileCode(scriptText, null, info);
        }

        /// <summary>
        /// Validates the specified information.
        /// </summary>
        /// <param name="info">The information.</param>
        protected virtual void Validate(CompileInfo info)
        { }

        Assembly CompileCode(string scriptText, string scriptFile, CompileInfo info)
        {
            Validate(info);

            // scriptFile is needed to allow injection of the debug information

            (byte[] asm, byte[] pdb) = Compile(scriptText, scriptFile, info);

            if (info?.PreferLoadingFromFile == true && info?.AssemblyFile.IsNotEmpty() == true)
            {
                // return Assembly.LoadFile(info.AssemblyFile);
                // this way the loaded script assembly can be referenced from
                // other scripts without custom asssembly probing
                return Assembly.LoadFrom(info.AssemblyFile);
            }
            else
            {
                if (pdb != null)
                    return AppDomain.CurrentDomain.Load(asm, pdb);
                else
                    return AppDomain.CurrentDomain.Load(asm);
            }
        }

        /// <summary>
        /// Returns set of referenced assemblies.
        /// <para>
        /// Notre: the set of assemblies is cleared on Reset.
        /// </para>
        /// </summary>
        /// <returns>The method result.</returns>
        /// <exception cref="NotImplementedException"></exception>
        public virtual Assembly[] GetReferencedAssemblies()
            => throw new NotImplementedException();

        /// <summary>
        /// Gets the referenced assemblies files.
        /// </summary>
        /// <returns>The method result.</returns>
        /// <exception cref="NotImplementedException"></exception>
        public virtual string[] GetReferencedAssembliesFiles()
            => throw new NotImplementedException();

        /// <summary>
        /// Compiles C# file (script) into assembly file. The C# contains typical C# code containing a single or multiple class definition(s).
        /// </summary>
        /// <param name="scriptFile">The C# script file.</param>
        /// <param name="outputFile">The path to the assembly file to be compiled.</param>
        /// <returns>
        /// The compiled assembly file path.
        /// </returns>
        /// <example>
        ///   <code>
        /// string asmFile = CSScript.Evaluator
        ///                          .CompileAssemblyFromFile("MyScript.cs", "MyScript.dll");
        /// </code>
        /// </example>
        public string CompileAssemblyFromFile(string scriptFile, string outputFile)
        {
            var info = new CompileInfo();
            info.AssemblyFile = Path.GetFullPath(outputFile);
            info.PdbFile = Path.ChangeExtension(info.AssemblyFile, ".pdb");

            Compile(null, scriptFile, info);
            return info.AssemblyFile;
        }

        /// <summary>
        /// Compiles C# code (script) into assembly file. The C# code is a typical C# code containing a single or multiple class definition(s).
        /// </summary>
        /// <example>
        ///<code>
        /// string asmFile = CSScript.Evaluator
        ///                          .CompileAssemblyFromCode(
        ///                                 @"using System;
        ///                                   public class Script
        ///                                   {
        ///                                       public int Sum(int a, int b)
        ///                                       {
        ///                                           return a+b;
        ///                                       }
        ///                                   }",
        ///                                   "MyScript.dll");
        /// </code>
        /// </example>
        /// <param name="scriptText">The C# script text.</param>
        /// <param name="outputFile">The path to the assembly file to be compiled.</param>
        /// <returns>The compiled assembly file path.</returns>
        public string CompileAssemblyFromCode(string scriptText, string outputFile)
        {
            var info = new CompileInfo();
            info.AssemblyFile = Path.GetFullPath(outputFile);
            info.PdbFile = Path.ChangeExtension(info.AssemblyFile, ".pdb");

            Compile(scriptText, null, info);
            return info.AssemblyFile;
        }

        /// <summary>
        /// Compiles the specified script text without loading it into the AppDomain or
        /// writing to the file system.
        /// </summary>
        /// <example>
        ///<code>
        /// try
        /// {
        ///     CSScript.Evaluator
        ///             .Check(@"using System;
        ///                      public class Script
        ///                      {
        ///                          public int Sum(int a, int b)
        ///                          {
        ///                              error
        ///                              return a+b;
        ///                          }
        ///                      }");
        /// }
        /// catch (Exception e)
        /// {
        ///     Console.WriteLine("Compile error: " + e.Message);
        /// }
        /// </code>
        /// </example>
        /// <param name="scriptText">The script text.</param>
        public void Check(string scriptText)
        {
            Compile(scriptText, null, null);
        }

        /// <summary>
        /// Compiles the specified script text.
        /// </summary>
        /// <param name="scriptText">The script text.</param>
        /// <param name="scriptFile">The script file.</param>
        /// <param name="info">The information.</param>
        /// <returns>The method result.</returns>
        /// <exception cref="NotImplementedException"></exception>
        protected virtual (byte[] asm, byte[] pdb) Compile(string scriptText, string scriptFile, CompileInfo info)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Wraps C# code fragment into auto-generated class (type name <c>DynamicClass</c>) and evaluates it.
        /// <para>
        /// This method is a logical equivalent of <see cref="CSScriptLib.IEvaluator.CompileCode"/> but is allows you to define
        /// your script class by specifying class method instead of whole class declaration.</para>
        /// </summary>
        /// <example>
        ///<code>
        /// dynamic script = CSScript.RoslynEvaluator
        ///                          .CompileMethod(@"int Sum(int a, int b)
        ///                                           {
        ///                                               return a+b;
        ///                                           }")
        ///                          .CreateObject("*");
        ///
        /// var result = script.Sum(7, 3);
        /// </code>
        /// </example>
        /// <param name="code">The C# code.</param>
        /// <returns>The compiled assembly.</returns>
        public Assembly CompileMethod(string code)
        {
            string scriptText = CSScript.WrapMethodToAutoClass(code, false, false);
            return CompileCode(scriptText);
        }

        /// <summary>
        /// Wraps C# code fragment into auto-generated class (type name <c>DynamicClass</c>), evaluates it and loads the class to the current AppDomain.
        /// <para>Returns non-typed <see cref="CSScriptLib.MethodDelegate"/> for class-less style of invoking.</para>
        /// </summary>
        /// <example>
        /// <code>
        /// var log = CSScript.RoslynEvaluator
        ///                   .CreateDelegate(@"void Log(string message)
        ///                                     {
        ///                                         Console.WriteLine(message);
        ///                                     }");
        ///
        /// log("Test message");
        /// </code>
        /// </example>
        /// <param name="code">The C# code.</param>
        /// <returns> The instance of a non-typed <see cref="CSScriptLib.MethodDelegate"/></returns>
        public MethodDelegate CreateDelegate(string code)
        {
            string scriptText = CSScript.WrapMethodToAutoClass(code, true, false);
            var asm = CompileCode(scriptText);
            var method = asm.GetTypes()
                            .Where(x => x.GetName().EndsWith("DynamicClass"))
                            .SelectMany(x => x.GetMethods())
                            .FirstOrDefault();

            object invoker(params object[] args)
            {
                return method.Invoke(null, args);
            }

            return invoker;
        }

        /// <summary>
        /// Wraps C# code fragment into auto-generated class (type name <c>DynamicClass</c>), evaluates it and loads the class to the current AppDomain.
        /// <para>Returns typed <see cref="CSScriptLib.MethodDelegate{T}"/> for class-less style of invoking.</para>
        /// </summary>
        /// <typeparam name="T">The delegate return type.</typeparam>
        /// <example>
        /// <code>
        /// var product = CSScript.RoslynEvaluator
        ///                       .CreateDelegate&lt;int&gt;(@"int Product(int a, int b)
        ///                                             {
        ///                                                 return a * b;
        ///                                             }");
        ///
        /// int result = product(3, 2);
        /// </code>
        /// </example>
        /// <param name="code">The C# code.</param>
        /// <returns> The instance of a typed <see cref="CSScriptLib.MethodDelegate{T}"/></returns>
        public MethodDelegate<T> CreateDelegate<T>(string code)
        {
            string scriptText = CSScript.WrapMethodToAutoClass(code, true, false);
            var asm = CompileCode(scriptText);
            var method = asm.GetTypes()
                            .Where(x => x.GetName().EndsWith("DynamicClass"))
                            .SelectMany(x => x.GetMethods())
                            .FirstOrDefault();

            T invoker(params object[] args)
            {
                return (T)method.Invoke(null, args);
            }

            return invoker;
        }

        /// <summary>
        /// Analyses the script code and returns set of locations for the assemblies referenced from the code with CS-Script directives (//css_ref).
        /// </summary>
        /// <param name="code">The script code.</param>
        /// <param name="searchDirs">The assembly search/probing directories.</param>
        /// <returns>Array of the referenced assemblies</returns>
        public string[] GetReferencedAssemblies(string code, params string[] searchDirs)
        {
            var retval = new List<string>();

            var parser = new CSharpParser(code);

            var globalProbingDirs = CSScript.GlobalSettings.SearchDirs
                                                           .Select(Environment.ExpandEnvironmentVariables)
                                                           .Where(x => x.Any());

            var dirs = searchDirs.Concat(parser.ExtraSearchDirs)
                                 .Concat(globalProbingDirs)
                                 .ToArray();

            dirs = dirs.Select(x => Path.GetFullPath(x)).Distinct().ToArray();

            var asms = new List<string>(parser.RefAssemblies);
            var unresolved_asms = new List<string>();

            foreach (var asm in asms)
            {
                var files = AssemblyResolver.FindAssembly(asm, dirs);
                if (files.Any())
                    retval.AddRange(files);
                else
                    unresolved_asms.Add(asm);
            }

            if (!parser.IgnoreNamespaces.Any(x => x == "*"))
                foreach (var asm in parser.RefNamespaces.Except(parser.IgnoreNamespaces))
                    foreach (string asmFile in AssemblyResolver.FindAssembly(asm, dirs))
                        retval.Add(asmFile);

            foreach (var asm in unresolved_asms)
                this.ReferenceAssemblyByName(asm);

            return retval.Distinct().ToArray();
        }

        /// <summary>
        /// Evaluates and loads C# code to the current AppDomain. Returns instance of the first class defined in the code.
        /// </summary>
        /// <example>The following is the simple example of the LoadCode usage:
        ///<code>
        /// dynamic script = CSScript.RoslynEvaluator
        ///                          .LoadCode(@"using System;
        ///                                      public class Script
        ///                                      {
        ///                                          public int Sum(int a, int b)
        ///                                          {
        ///                                              return a+b;
        ///                                          }
        ///                                      }");
        /// int result = script.Sum(1, 2);
        /// </code>
        /// </example>
        /// <param name="scriptText">The C# script text.</param>
        /// <param name="args">The non default constructor arguments.</param>
        /// <returns>Instance of the class defined in the script.</returns>
        public object LoadCode(string scriptText, params object[] args)
        {
            return CompileCode(scriptText).CreateObject(ExtractClassName(scriptText), args);
        }

        static string ExtractClassName(string scriptText)
        {
            // will need to use Roslyn eventually
            return "*";
        }

        internal object LoadCodeByName(string scriptText, string className, params object[] args)
        {
            return CompileCode(scriptText).CreateObject(className, args);
        }

        /// <summary>
        /// Evaluates and loads C# code to the current AppDomain. Returns instance of the first class defined in the code.
        /// </summary>
        /// <typeparam name="T">The type of the script class instance should be type casted to.</typeparam>
        /// <param name="scriptText">The C# script text.</param>
        /// <param name="args">The non default type <c>T</c> constructor arguments.</param>
        /// <returns>
        /// Aligned to the <c>T</c> interface instance of the class defined in the script.
        /// </returns>
        /// <example>The following is the simple example of the interface alignment:
        /// <code>
        /// public interface ICalc
        /// {
        ///     int Sum(int a, int b);
        /// }
        /// ....
        /// ICalc calc = CSScript.Evaluator
        ///                      .LoadCode&lt;ICalc&gt;(@"using System;
        ///                                         public class Script
        ///                                         {
        ///                                             public int Sum(int a, int b)
        ///                                             {
        ///                                                 return a+b;
        ///                                             }
        ///                                         }");
        /// int result = calc.Sum(1, 2);
        /// </code></example>
        public T LoadCode<T>(string scriptText, params object[] args) where T : class
        {
            this.ReferenceAssemblyOf<T>();
            var asm = CompileCode(scriptText);
            var type = asm.FirstUserTypeAssignableFrom<T>();
            return (T)asm.CreateObject(type.FullName, args);
        }

        /// <summary>
        /// Wraps C# code fragment into auto-generated class (type name <c>DynamicClass</c>), evaluates it and loads
        /// the class to the current AppDomain.
        /// <para>Returns instance of <c>T</c> delegate for the first method in the auto-generated class.</para>
        /// </summary>
        ///  <example>
        /// <code>
        /// var Product = CSScript.Evaluator
        ///                       .LoadDelegate&lt;Func&lt;int, int, int&gt;&gt;(
        ///                                   @"int Product(int a, int b)
        ///                                     {
        ///                                         return a * b;
        ///                                     }");
        ///
        /// int result = Product(3, 2);
        /// </code>
        /// </example>
        /// <param name="code">The C# code.</param>
        /// <returns>Instance of <c>T</c> delegate.</returns>
        public T LoadDelegate<T>(string code) where T : class
        {
            throw new NotImplementedException("You may want to consider using interfaces with LoadCode/LoadMethod or use CreateDelegate instead.");
            //string scriptText = CSScript.WrapMethodToAutoClass(code, true, false);
            //Assembly asm = CompileCode(scriptText);
            //var method = asm.GetTypes().First(t => t.Name == "DynamicClass").GetMethods().First();
            //return System.Delegate.CreateDelegate(typeof(T), method) as T;
        }

        /// <summary>
        /// Evaluates and loads C# code from the specified file to the current AppDomain. Returns instance of the first
        /// class defined in the script file.
        /// </summary>
        /// <example>The following is the simple example of the interface alignment:
        ///<code>
        /// dynamic script = CSScript.RoslynEvaluator
        ///                          .LoadFile("calc.cs");
        ///
        /// int result = script.Sum(1, 2);
        /// </code>
        /// </example>/// <param name="scriptFile">The C# script file.</param>
        /// <param name="args">Optional non-default constructor arguments.</param>
        /// <returns>Instance of the class defined in the script file.</returns>
        public object LoadFile(string scriptFile, params object[] args)
        {
            var code = File.ReadAllText(scriptFile);
            return CompileCode(code, scriptFile, null).CreateObject(ExtractClassName(code), args);
        }

        /// <summary>
        /// Evaluates and loads C# code from the specified file to the current AppDomain. Returns instance of the first
        /// class defined in the script file.
        /// After initializing the class instance it is aligned to the interface specified by the parameter <c>T</c>.
        /// <para><c>Note:</c> the script class does not have to inherit from the <c>T</c> parameter as the proxy type
        /// will be generated anyway.</para>
        /// </summary>
        /// <example>The following is the simple example of the interface alignment:
        ///<code>
        /// public interface ICalc
        /// {
        ///     int Sum(int a, int b);
        /// }
        /// ....
        /// ICalc calc = CSScript.Evaluator
        ///                      .LoadFile&lt;ICalc&gt;("calc.cs");
        ///
        /// int result = calc.Sum(1, 2);
        /// </code>
        /// </example>
        /// <typeparam name="T">The type of the interface type the script class instance should be aligned to.</typeparam>
        /// <param name="scriptFile">The C# script text.</param>
        /// <param name="args">Optional non-default constructor arguments.</param>
        /// <returns>Aligned to the <c>T</c> interface instance of the class defined in the script file.</returns>
        public T LoadFile<T>(string scriptFile, params object[] args) where T : class
        {
            var asm = CompileCode(File.ReadAllText(scriptFile), scriptFile, null);
            var type = asm.FirstUserTypeAssignableFrom<T>();
            return (T)asm.CreateObject(type.FullName, args);
        }

        /// <summary>
        /// Wraps C# code fragment into auto-generated class (type name <c>DynamicClass</c>), evaluates it and loads
        /// the class to the current AppDomain.
        /// </summary>
        /// <example>The following is the simple example of the LoadMethod usage:
        /// <code>
        /// dynamic script = CSScript.RoslynEvaluator
        ///                          .LoadMethod(@"int Product(int a, int b)
        ///                                        {
        ///                                            return a * b;
        ///                                        }");
        ///
        /// int result = script.Product(3, 2);
        /// </code>
        /// </example>
        /// <param name="code">The C# script text.</param>
        /// <returns>Instance of the first class defined in the script.</returns>
        public object LoadMethod(string code)
        {
            string scriptText = CSScript.WrapMethodToAutoClass(code, false, false);
            return LoadCodeByName(scriptText, $"*.{Globals.DynamicWrapperClassName}");
        }

        /// <summary>
        /// Wraps C# code fragment into auto-generated class (type name <c>DynamicClass</c>), evaluates it and loads
        /// the class to the current AppDomain.
        /// <para>
        /// After initializing the class instance it is aligned to the interface specified by the parameter <c>T</c>.
        /// </para>
        /// </summary>
        /// <example>The following is the simple example of the interface alignment:
        /// <code>
        /// public interface ICalc
        /// {
        ///     int Sum(int a, int b);
        ///     int Div(int a, int b);
        /// }
        /// ....
        /// ICalc script = CSScript.RoslynEvaluator
        ///                        .LoadMethod&lt;ICalc&gt;(@"public int Sum(int a, int b)
        ///                                             {
        ///                                                 return a + b;
        ///                                             }
        ///                                             public int Div(int a, int b)
        ///                                             {
        ///                                                 return a/b;
        ///                                             }");
        /// int result = script.Div(15, 3);
        /// </code>
        /// </example>
        /// <typeparam name="T">The type of the interface type the script class instance should be aligned to.</typeparam>
        /// <param name="code">The C# script text.</param>
        /// <returns>Aligned to the <c>T</c> interface instance of the auto-generated class defined in the script.</returns>
        public T LoadMethod<T>(string code) where T : class
        {
            string scriptText = CSScript.WrapMethodToAutoClass(code, false, false, typeof(T).FullName);
            return LoadCode<T>(scriptText);
        }

        /// <summary>
        /// Gets or sets the flag indicating if the script code should be analyzed and the assemblies
        /// that the script depend on (via '//css_...' and 'using ...' directives) should be referenced.
        /// </summary>
        /// <value></value>
        public bool DisableReferencingFromCode { get; set; }

        /// <summary>
        /// References the assemblies from the script code.
        /// <para>The method analyses and tries to resolve CS-Script directives (e.g. '//css_ref') and 'used' namespaces based on the
        /// optional search directories.</para>
        /// </summary>
        /// <param name="code">The script code.</param>
        /// <param name="searchDirs">The assembly search/probing directories.</param>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
        public IEvaluator ReferenceAssembliesFromCode(string code, params string[] searchDirs)
        {
            foreach (var asm in GetReferencedAssemblies(code, searchDirs))
                ReferenceAssembly(asm);
            return this;
        }

        /// <summary>
        /// References the given assembly by the assembly path.
        /// <para>It is safe to call this method multiple times for the same assembly. If the assembly already referenced it will not
        /// be referenced again.</para>
        /// </summary>
        /// <param name="assembly">The path to the assembly file.</param>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
        public IEvaluator ReferenceAssembly(string assembly)
        {
            var globalProbingDirs = CSScript.GlobalSettings.SearchDirs.ToList();

            //zos
            //globalProbingDirs.Add(Assembly.GetCallingAssembly().GetAssemblyDirectoryName());

            var dirs = globalProbingDirs.ToArray();

            string asmFile = AssemblyResolver.FindAssembly(assembly, dirs).FirstOrDefault();
            if (asmFile == null)
                throw new Exception("Cannot find referenced assembly '" + assembly + "'");

            ReferenceAssembly(Assembly.LoadFile(asmFile));
            return this;
        }

        /// <summary>
        /// Gets the name of the engine (e.g. 'csc' or 'dotnet').
        /// </summary>
        /// <value>
        /// The name of the engine.
        /// </value>
        protected virtual string EngineName => "CS-Script evaluator";

        /// <summary>
        /// References the given assembly.
        /// <para>It is safe to call this method multiple times
        /// for the same assembly. If the assembly already referenced it will not
        /// be referenced again.
        /// </para>
        /// </summary>
        /// <param name="assembly">The assembly instance.</param>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
        public virtual IEvaluator ReferenceAssembly(Assembly assembly)
        => throw new NotImplementedException();

        /// <summary>
        /// References the name of the assembly by its partial name.
        /// <para>Note that the referenced assembly will be loaded into the host AppDomain in order to resolve assembly partial name.</para>
        /// <para>It is an equivalent of <c>Evaluator.ReferenceAssembly(Assembly.Load(assemblyPartialName))</c></para>
        /// </summary>
        /// <param name="assemblyName">Name of the assembly.</param>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
        public IEvaluator ReferenceAssemblyByName(string assemblyName)
        {
            return ReferenceAssembly(Assembly.Load(assemblyName));
        }

        /// <summary>
        /// References the assembly by the given namespace it implements.
        /// </summary>
        /// <param name="namespace">The namespace.</param>
        /// <param name="resolved">Set to <c>true</c> if the namespace was successfully resolved (found) and
        /// the reference was added; otherwise, <c>false</c>.</param>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
        public IEvaluator TryReferenceAssemblyByNamespace(string @namespace, out bool resolved)
        {
            resolved = false;
            foreach (string asm in AssemblyResolver.FindGlobalAssembly(@namespace))
            {
                resolved = true;
                ReferenceAssembly(asm);
            }
            return this;
        }

        /// <summary>
        /// References the assembly by the given namespace it implements.
        /// <para>Adds assembly reference if the namespace was successfully resolved (found) and, otherwise does nothing</para>
        /// </summary>
        /// <param name="namespace">The namespace.</param>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
        public IEvaluator ReferenceAssemblyByNamespace(string @namespace)
        {
            foreach (string asm in AssemblyResolver.FindGlobalAssembly(@namespace))
                ReferenceAssembly(asm);
            return this;
        }

        /// <summary>
        /// References the assembly by the object, which belongs to this assembly.
        /// <para>It is safe to call this method multiple times
        /// for the same assembly. If the assembly already referenced it will not
        /// be referenced again.
        /// </para>
        /// </summary>
        /// <param name="obj">The object, which belongs to the assembly to be referenced.</param>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
        public IEvaluator ReferenceAssemblyOf(object obj)
        {
            ReferenceAssembly(obj.GetType().Assembly);
            return this;
        }

        /// <summary>
        /// References the assembly by the object, which belongs to this assembly.
        /// <para>It is safe to call this method multiple times
        /// for the same assembly. If the assembly already referenced it will not
        /// be referenced again.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The type which is implemented in the assembly to be referenced.</typeparam>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
        public IEvaluator ReferenceAssemblyOf<T>()
        {
            return ReferenceAssembly(typeof(T).Assembly);
        }

#if net35
        /// <summary>
        /// References the assemblies the are already loaded into the current <c>AppDomain</c>.
        /// <para>This method is an equivalent of <see cref="CSScriptLib.IEvaluator.ReferenceDomainAssemblies"/>
        /// with the hard codded <c>DomainAssemblies.AllStaticNonGAC</c> input parameter.
        /// </para>
        /// </summary>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
        public IEvaluator ReferenceDomainAssemblies()
        {
            return ReferenceDomainAssemblies(DomainAssemblies.AllStaticNonGAC);
        }
#endif

        /// <summary>
        /// References the assemblies the are already loaded into the current <c>AppDomain</c>.
        /// </summary>
        /// <param name="assemblies">The type of assemblies to be referenced.</param>
        /// <returns>The instance of the <see cref="CSScriptLib.IEvaluator"/> to allow  fluent interface.</returns>
#if net35
        public IEvaluator ReferenceDomainAssemblies(DomainAssemblies assemblies)
#else

        public IEvaluator ReferenceDomainAssemblies(DomainAssemblies assemblies = DomainAssemblies.AllStaticNonGAC)
#endif
        {
            //NOTE: It is important to avoid loading the runtime itself (mscorelib) as it
            //will break the code evaluation (compilation).
            //
            //On .NET mscorelib is filtered out by GlobalAssemblyCache check but
            //on Mono it passes through so there is a need to do a specific check for mscorelib assembly.
            var relevantAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            if (assemblies == DomainAssemblies.AllStatic)
            {
                relevantAssemblies = relevantAssemblies.Where(x => !x.IsDynamic() && x != mscorelib).ToArray();
            }
            else if (assemblies == DomainAssemblies.AllStaticNonGAC)
            {
                relevantAssemblies = relevantAssemblies.Where(x => !x.GlobalAssemblyCache && !x.IsDynamic() && x != mscorelib).ToArray();
            }
            else if (assemblies == DomainAssemblies.None)
            {
                relevantAssemblies = new Assembly[0];
            }

            foreach (var asm in relevantAssemblies)
                ReferenceAssembly(asm);

            return this;
        }

        /// <summary>
        /// Resets Evaluator.
        /// <para>
        /// Resetting means clearing all referenced assemblies, recreating evaluation infrastructure (e.g. compiler setting)
        /// and reconnection to or recreation of the underlying compiling services.
        /// </para>
        /// <para>Optionally the default current AppDomain assemblies can be referenced automatically with
        /// <paramref name="referenceDomainAssemblies"/>.</para>
        /// </summary>
        /// <param name="referenceDomainAssemblies">if set to <c>true</c> the default assemblies of the current AppDomain
        /// will be referenced (see <see cref="ReferenceDomainAssemblies(DomainAssemblies)"/> method).
        /// </param>
        /// <returns>The freshly initialized instance of the <see cref="CSScriptLib.IEvaluator"/>.</returns>
        public virtual IEvaluator Reset(bool referenceDomainAssemblies = true)
        {
            if (referenceDomainAssemblies)
                ReferenceDomainAssemblies();

            return this;
        }
    }
}