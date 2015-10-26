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
		/// On each tick calls registered mods and aborts the tick early if requested.
		/// </summary>
		/// <returns><c>true</c>, If requested to abort early, <c>false</c> otherwise.</returns>
		/// <param name="caller">Caller.</param>
		/// <param name="timeStep">Time step.</param>
		public bool onTick(Object caller, float timeStep) {
			Console.WriteLine ("On tick called from: " + caller);
			return false;
		}

		/// <summary>
		/// On each tick calls registered mods and aborts the tick early if requested.
		/// </summary>
		/// <returns><c>true</c>, If requested to abort early, <c>false</c> otherwise.</returns>
		/// <param name="caller">Caller.</param>
		public bool onTick(Object caller) {
			Console.WriteLine ("On tick called from: " + caller);
			return false;
		}

		/// <summary>
		/// On each update calls registered mods and aborts the tick early if requested.
		/// </summary>
		/// <returns><c>true</c>, If requested to abort early, <c>false</c> otherwise.</returns>
		/// <param name="caller">Caller.</param>
		/// <param name="timeStep">Time step.</param>
		public bool onUpdate(Object caller, float timeStep) {
			Console.WriteLine ("On update called from: " + caller);
			return false;
		}

		/// <summary>
		/// On each update calls registered mods and aborts the tick early if requested.
		/// </summary>
		/// <returns><c>true</c>, If requested to abort early, <c>false</c> otherwise.</returns>
		/// <param name="caller">Caller.</param>
		public bool onUpdate(Object caller) {
			Console.WriteLine ("On update called from: " + caller);
			return false;
		}
	}
}

