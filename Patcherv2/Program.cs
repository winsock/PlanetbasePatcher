using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

using System.Linq;

namespace Patcherv2
{
	class MainClass
	{
		private ModuleDefinition assemblyModule;
		private ModuleDefinition patchAssembly;
		private TypeDefinition gameManager;
		FieldDefinition loaderInstanceField;

		public static void Main (string[] args)
		{
			new MainClass ();
		}

		public MainClass ()
		{
			DefaultAssemblyResolver assemblies = new DefaultAssemblyResolver ();
			assemblies.AddSearchDirectory (AppDomain.CurrentDomain.BaseDirectory);
			assemblyModule = ModuleDefinition.ReadModule (AppDomain.CurrentDomain.BaseDirectory + @"Assembly-CSharp.dll", new ReaderParameters { AssemblyResolver = assemblies });
			patchAssembly = ModuleDefinition.ReadModule (AppDomain.CurrentDomain.BaseDirectory + @"PlanetbasePatch.dll", new ReaderParameters { AssemblyResolver = assemblies }); 
			gameManager = assemblyModule.Types.First (t => t.FullName == "Planetbase.GameManager");
			AddPublicInstanceField ();

			MethodDefinition gameManagerCtor = gameManager.Methods.First (m => m.IsConstructor);
			ILProcessor ilProcessor = gameManagerCtor.Body.GetILProcessor ();

			ilProcessor.InsertBefore (gameManagerCtor.Body.Instructions.Last(), ilProcessor.Create (OpCodes.Newobj, assemblyModule.Import(patchAssembly.Types.First (t => t.FullName == "PlanetbasePatch.Loader").Methods.First (m => m.IsConstructor && !m.HasParameters))));
			ilProcessor.InsertBefore (gameManagerCtor.Body.Instructions.Last(), ilProcessor.Create (OpCodes.Stsfld, loaderInstanceField));

			Save ();
		}

		private void AddPublicInstanceField () {
			loaderInstanceField = new FieldDefinition ("loaderInstance", FieldAttributes.Static | FieldAttributes.Public, assemblyModule.Import (patchAssembly.Types.First (t => t.FullName == "PlanetbasePatch.Loader")));
			gameManager.Fields.Add (loaderInstanceField);
			assemblyModule.Import (gameManager);
		}

		private void Save() {
			assemblyModule.Write (AppDomain.CurrentDomain.BaseDirectory + @"Assembly-CSharp.dll.patched");
		}
	}
}
