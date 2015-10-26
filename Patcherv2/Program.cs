using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

using System.Linq;

using System.Xml.Linq;
using System.Xml.Schema;

namespace Patcherv2
{
	class MainClass
	{
		private String versionString;

		private ModuleDefinition assemblyModule;
		private ModuleDefinition patchAssembly;
		private TypeDefinition gameManager;
		private FieldDefinition loaderInstanceField;

		private XmlSchemaSet schemaSet = new XmlSchemaSet();
		private System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();

		public static void Main (string[] args) {
			new MainClass ();
		}

		public MainClass () {
			schemaSet.Add (XmlSchema.Read (assembly.GetManifestResourceStream ("Patcherv2.Resources.ClassesToHook.xsd"), (object sender, ValidationEventArgs e) => {}));

			DefaultAssemblyResolver assemblies = new DefaultAssemblyResolver ();
			assemblies.AddSearchDirectory (AppDomain.CurrentDomain.BaseDirectory);
			assemblyModule = ModuleDefinition.ReadModule (AppDomain.CurrentDomain.BaseDirectory + @"Assembly-CSharp.dll", new ReaderParameters { AssemblyResolver = assemblies });
			versionString = assemblyModule.Types.FirstOrDefault (t => t.FullName == "Planetbase.Definitions").Fields.FirstOrDefault (f => f.Name == "VersionNumber" && f.HasConstant).Constant as String;
			if (versionString == null) {
				Console.WriteLine("Unable to read version from assembly! Aborting!");
				return;
			}
			Console.WriteLine ("Attempting to patch version: " + versionString);

			patchAssembly = ModuleDefinition.ReadModule (AppDomain.CurrentDomain.BaseDirectory + @"PlanetbasePatch.dll", new ReaderParameters { AssemblyResolver = assemblies }); 
			gameManager = assemblyModule.Types.FirstOrDefault (t => t.FullName == "Planetbase.GameManager");

			AddPublicInstanceField ();
			hookUpdateOrTickMethods ();

			MethodDefinition gameManagerCtor = gameManager.Methods.First (m => m.IsConstructor);
			ILProcessor ilProcessor = gameManagerCtor.Body.GetILProcessor ();

			ilProcessor.InsertBefore (gameManagerCtor.Body.Instructions.Last(), ilProcessor.Create (OpCodes.Newobj, assemblyModule.Import(patchAssembly.Types.FirstOrDefault (t => t.FullName == "PlanetbasePatch.Loader").Methods.FirstOrDefault (m => m.IsConstructor && !m.HasParameters))));
			ilProcessor.InsertBefore (gameManagerCtor.Body.Instructions.Last(), ilProcessor.Create (OpCodes.Stsfld, loaderInstanceField));

			Save ();
		}

		private void hookUpdateOrTickMethods () {
			var classesToHook = XDocument.Load (assembly.GetManifestResourceStream ("Patcherv2.Resources.ClassesToHook.xml"));
			classesToHook.Validate (schemaSet, (sender, e) => {
				if (e != null) {
					Console.WriteLine("Error in internal XML! Report to Github");
					Console.WriteLine(e.Message);
					Environment.Exit (-1);
				}
			}, true);

			MethodReference onUpdate2Arg = assemblyModule.Import (patchAssembly.Types.FirstOrDefault (t => t.FullName == "PlanetbasePatch.Loader").Methods.FirstOrDefault (m => m.Name == "onUpdate" && m.Parameters.Count == 2));
			MethodReference onUpdate1Arg = assemblyModule.Import (patchAssembly.Types.FirstOrDefault (t => t.FullName == "PlanetbasePatch.Loader").Methods.FirstOrDefault (m => m.Name == "onUpdate" && m.Parameters.Count == 1));

			MethodReference onTick2Arg = assemblyModule.Import (patchAssembly.Types.FirstOrDefault (t => t.FullName == "PlanetbasePatch.Loader").Methods.FirstOrDefault (m => m.Name == "onTick" && m.Parameters.Count == 2));
			MethodReference onTick1Arg = assemblyModule.Import (patchAssembly.Types.FirstOrDefault (t => t.FullName == "PlanetbasePatch.Loader").Methods.FirstOrDefault (m => m.Name == "onTick" && m.Parameters.Count == 1));

			var planetbaseHookVersion = classesToHook.Root.Elements ().First (e => e.Attribute("name").Value == "Planetbase" && e.Attribute ("version").Value == versionString);
			foreach (XElement gameClass in planetbaseHookVersion.Elements()) {
				TypeDefinition typeDef = assemblyModule.Types.FirstOrDefault(t => t.FullName == "Planetbase." + gameClass.Attribute("name").Value);
				if (typeDef == null) {
					Console.WriteLine("Error finding class: Planetbase." + gameClass.Attribute("name").Value);
					Console.WriteLine ("Skipping class");
					continue;
				}

				MethodDefinition updateMethod = typeDef.Methods.FirstOrDefault (m => m.Name == "update");
				MethodDefinition tickMethod = typeDef.Methods.FirstOrDefault (m => m.Name == "tick");
				if (tickMethod == null && updateMethod == null) {
					continue;
				}

				if (updateMethod != null && !updateMethod.IsStatic) {
					ILProcessor updateIL = updateMethod.Body.GetILProcessor ();

					var loadField = updateIL.Create (OpCodes.Ldsfld, loaderInstanceField);
					var loadThis = updateIL.Create (OpCodes.Ldarg_0);
					var loadTimeStep = updateIL.Create (OpCodes.Ldarg_1);
					var callUpdate = updateIL.Create (OpCodes.Call, (updateMethod.Parameters.Count != 1 || updateMethod.Parameters[0].ParameterType.MetadataType != MetadataType.Single) ? onUpdate1Arg : onUpdate2Arg);
					var branch = updateIL.Create (OpCodes.Brtrue, updateMethod.Body.Instructions.Last());

					updateIL.InsertBefore (updateMethod.Body.Instructions.First (), loadField);
					updateIL.InsertAfter (loadField, loadThis);
					if (updateMethod.Parameters.Count == 1 && updateMethod.Parameters[0].ParameterType.MetadataType == MetadataType.Single) {
						updateIL.InsertAfter (loadThis, loadTimeStep);
						updateIL.InsertAfter (loadTimeStep, callUpdate);
					} else {
						updateIL.InsertAfter (loadThis, callUpdate);
					}
					updateIL.InsertAfter (callUpdate, branch);
				}

				if (tickMethod != null && !tickMethod.IsStatic) {
					ILProcessor tickIl = tickMethod.Body.GetILProcessor ();

					var loadField = tickIl.Create (OpCodes.Ldsfld, loaderInstanceField);
					var loadThis = tickIl.Create (OpCodes.Ldarg_0);
					var loadTimeStep = tickIl.Create (OpCodes.Ldarg_1);
					var callTick = tickIl.Create (OpCodes.Call, (tickMethod.Parameters.Count != 1 || tickMethod.Parameters[0].ParameterType.MetadataType != MetadataType.Single) ? onTick1Arg : onTick2Arg);
					var branch = tickIl.Create (OpCodes.Brtrue, tickMethod.Body.Instructions.Last());

					tickIl.InsertBefore (tickMethod.Body.Instructions.First (), loadField);
					tickIl.InsertAfter (loadField, loadThis);
					if (tickMethod.Parameters.Count == 1 && tickMethod.Parameters[0].ParameterType.MetadataType == MetadataType.Single) {
						tickIl.InsertAfter (loadThis, loadTimeStep);
						tickIl.InsertAfter (loadTimeStep, callTick);
					} else {
						tickIl.InsertAfter (loadThis, callTick);
					}
					tickIl.InsertAfter (callTick, branch);
				}
			}
		}

		private void AddPublicInstanceField () {
			loaderInstanceField = new FieldDefinition ("loaderInstance", FieldAttributes.Static | FieldAttributes.Public, assemblyModule.Import (patchAssembly.Types.FirstOrDefault (t => t.FullName == "PlanetbasePatch.Loader")));
			gameManager.Fields.Add (loaderInstanceField);
			assemblyModule.Import (gameManager);
		}

		private void Save() {
			// For now do not overwrite the non-patched dll
			assemblyModule.Write (AppDomain.CurrentDomain.BaseDirectory + @"Assembly-CSharp.patched.dll");
		}
	}
}
