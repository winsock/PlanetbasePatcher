using System;

namespace PlanetbasePatch
{
	public class Loader
	{
		public Loader ()
		{
			Console.WriteLine ("Hello World!!!");
		}

		/// <summary>
		/// On each callback call any registered mods.
		/// </summary>
		/// <returns></returns>
		/// <param name="methodSig">Method signature</param>
		/// <param name="caller">Caller</param>
		/// <param name = "args"></param>
		public Object methodCallback(String methodSig, Object caller, params Object[] args) {
			Console.WriteLine ("Method callback called from: " + methodSig + " with " + args.Length + " arguments");
			return false;
		}
	}
}