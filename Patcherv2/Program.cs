using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

using System.Linq;

using System.Xml.Linq;
using System.Xml.Schema;
using Mono.Cecil.Rocks;

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

		private MethodReference callbackRef;

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
			callbackRef = assemblyModule.Import(typeof(PlanetbasePatch.Loader).GetMethod("methodCallback"));

			AddPublicInstanceField ();
			hookGameClasses ();

			MethodDefinition gameManagerCtor = gameManager.Methods.First (m => m.IsConstructor);
			ILProcessor ilProcessor = gameManagerCtor.Body.GetILProcessor ();

			ilProcessor.InsertBefore (gameManagerCtor.Body.Instructions.Last(), ilProcessor.Create (OpCodes.Newobj, assemblyModule.Import(patchAssembly.Types.FirstOrDefault (t => t.FullName == "PlanetbasePatch.Loader").Methods.FirstOrDefault (m => m.IsConstructor && !m.HasParameters))));
			ilProcessor.InsertBefore (gameManagerCtor.Body.Instructions.Last(), ilProcessor.Create (OpCodes.Stsfld, loaderInstanceField));

			Save ();
		}

		private void modifyClass (XElement classElement) {
			String classFullName = "Planetbase." + classElement.Attribute ("name").Value;
			bool parseAllThatStartWithName = classElement.Attribute ("matchAll") != null && Boolean.Parse (classElement.Attribute ("matchAll").Value);

			TypeDefinition typeDef = assemblyModule.Types.FirstOrDefault (t => t.FullName == classFullName);
			if (typeDef == null) {
				Console.WriteLine("Error finding class: " + classFullName);
				Console.WriteLine ("Skipping class");
				return;
			}

			// Hook basic methods
			hookUpdatesAndTicks (typeDef);
			// Hook explicitly requested methods
			if (classElement.HasElements) {
				parseRequestedMethods (classElement.Elements (), typeDef);
			}

			if (parseAllThatStartWithName) {
				// Hook any aditional methods that start with the class name
				foreach (TypeDefinition subClassDef in assemblyModule.Types.Where(t => t.FullName.StartsWith(classFullName) && t.FullName != classFullName)) {
					hookUpdatesAndTicks (subClassDef);
					// Hook explicitly requested methods
					if (classElement.HasElements) {
						parseRequestedMethods (classElement.Elements (), subClassDef);
					}
				}
			}
		}

		private void parseRequestedMethods(System.Collections.Generic.IEnumerable<XElement> requestedMethods, TypeDefinition classToHook) {
			foreach (XElement methodElement in requestedMethods) {
				String methodName = methodElement.Attribute ("name").Value;
				String returnTypeString = methodElement.Attribute ("return") == null ? "Void" : methodElement.Attribute ("return").Value;
				var possibleMethods = classToHook.Methods.Where (m => m.Name == methodName && m.ReturnType.Name == returnTypeString);

				String[] paramTypes = null;
				if (methodElement.HasElements) {
					paramTypes = new String[methodElement.Elements ().Count()];
					for (int i = 0; i < paramTypes.Count(); i++) {
						paramTypes [i] = methodElement.Elements ().ElementAt (i).Name.ToString();
					}
				}

				var possibleMethodArray = possibleMethods as object[] ?? possibleMethods.ToArray ();
				if (paramTypes == null && possibleMethodArray.Count () > 1) {
					Console.WriteLine ("Ambigious method: " + methodName + " to hook in class: " + classToHook.FullName);
					Console.WriteLine ("Skipping method(s)");
					continue;
				} else if (!possibleMethodArray.Any ()) {
					Console.WriteLine ("Unable to find method: " + methodName + " to hook in class: " + classToHook.FullName);
					Console.WriteLine ("Skipping method");
					continue;
				}

				foreach (MethodDefinition possibleMethodToHook in possibleMethodArray) {
					bool match = true;

					if (possibleMethodToHook.Parameters.Count > 0 && paramTypes != null && possibleMethodToHook.Parameters.Count == paramTypes.Count ()) {
						for (int i = 0; i < paramTypes.Count(); i++) {
							if (paramTypes [i] != possibleMethodToHook.Parameters [i].Name) {
								match = false;
								break;
							}
						}
					}
					if (match) {
						// Found method break stop trying to find a method
						hookMethod (possibleMethodToHook);
						break;
					}
				}
			}
		}

		private void hookGameClasses () {
			var classesToHook = XDocument.Load (assembly.GetManifestResourceStream ("Patcherv2.Resources.ClassesToHook.xml"));
			classesToHook.Validate (schemaSet, (sender, e) => {
				if (e != null) {
					Console.WriteLine("Error in internal XML! Report to Github");
					Console.WriteLine(e.Message);
					Environment.Exit (-1);
				}
			}, true);


			var planetbaseHookVersion = classesToHook.Root.Elements ().First (e => e.Attribute("name").Value == "Planetbase" && e.Attribute ("version").Value == versionString);
			foreach (XElement gameClass in planetbaseHookVersion.Elements()) {
				modifyClass (gameClass);
			}
		}

		private void hookMethod(MethodDefinition methodDef) {
			ILProcessor methodIL = methodDef.Body.GetILProcessor ();
			var objectArrayLocal = new VariableDefinition ("objectArray", assemblyModule.Import(typeof(Object[])));
			methodDef.Body.Variables.Add (objectArrayLocal);

			// Three instructions per array element, plus 4 instructions.
			Instruction[] callbackParamaters = new Instruction[(methodDef.Parameters.Count * 4) + 3];
			//var branch = updateIL.Create (OpCodes.Brtrue, updateMethod.Body.Instructions.Last());

			callbackParamaters [0] = methodIL.Create (OpCodes.Ldc_I4, methodDef.Parameters.Count);
			callbackParamaters [1] = methodIL.Create (OpCodes.Newarr, assemblyModule.Import(typeof(Object)));
			callbackParamaters [2] = methodIL.Create (OpCodes.Stloc, objectArrayLocal);

			for (int i = 0; i < methodDef.Parameters.Count; i++) {
				// Add five as an offset for the first for instructions.
				// If non-static we add one to the index of the argument to load since arg.0 is a pointer to "this"
				// If static the first argument passed to the function starts at arg.0
				callbackParamaters [((i * 4) + 0) + 3] = methodIL.Create (OpCodes.Ldloc, objectArrayLocal);
				callbackParamaters [((i * 4) + 1) + 3] = methodIL.Create (OpCodes.Ldc_I4, i);
				callbackParamaters [((i * 4) + 2) + 3] = methodIL.Create (OpCodes.Ldarg, i + (methodDef.IsStatic ? 0 : 1));
				callbackParamaters [((i * 4) + 3) + 3] = methodIL.Create (OpCodes.Stelem_Any, methodDef.Parameters[i].ParameterType);
			}

			Instruction previousInstruction = null;
			for (int i = 0; i < callbackParamaters.Count(); i++) {
				if (previousInstruction == null) {
					methodIL.InsertBefore (methodDef.Body.Instructions.First (), callbackParamaters[i]);
				} else {
					methodIL.InsertAfter (previousInstruction, callbackParamaters[i]);
				}
				previousInstruction = callbackParamaters[i];
			}
			var loadField = methodIL.Create (OpCodes.Ldsfld, loaderInstanceField);
			var methodDefString = methodIL.Create (OpCodes.Ldstr, methodDef.ToString ());
			// If static pass null as the object instance.
			var instance = methodDef.IsStatic ? methodIL.Create (OpCodes.Ldnull) : methodIL.Create (OpCodes.Ldarg_0);
			var ldLocalArray = methodIL.Create (OpCodes.Ldloc, objectArrayLocal);
			var callback = methodIL.Create (OpCodes.Call, callbackRef);

			methodIL.InsertAfter (previousInstruction, loadField);
			methodIL.InsertAfter (loadField, methodDefString);
			methodIL.InsertAfter (methodDefString, instance);
			methodIL.InsertAfter (instance, ldLocalArray);
			methodIL.InsertAfter (ldLocalArray, callback);
			// Need to pop the return value if not using it.
			methodIL.InsertAfter (callback, methodIL.Create(OpCodes.Pop));

			methodDef.Body.OptimizeMacros ();
		}

		private void hookUpdatesAndTicks(TypeDefinition typeDef) {
			MethodDefinition updateMethod = typeDef.Methods.FirstOrDefault (m => m.Name == "update");
			MethodDefinition tickMethod = typeDef.Methods.FirstOrDefault (m => m.Name == "tick");
			if (updateMethod != null) {
				hookMethod (updateMethod);
			}
			if (tickMethod != null) {
				hookMethod (tickMethod);
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
