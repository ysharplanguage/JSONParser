using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// JSON.NET 5.0 r8
using Newtonsoft.Json;

// Our stuff
using System.Text.Json;

namespace Test
{
	class Program
	{
		const string TINY_TEST_FILE_PATH = @"..\..\TestData\tiny.json.txt";
		const string SMALL_TEST_FILE_PATH = @"..\..\TestData\small.json.txt";
		const string FATHERS_TEST_FILE_PATH = @"..\..\TestData\fathers.json.txt";
		const string HUGE_TEST_FILE_PATH = @"..\..\TestData\huge.json.txt";

		/* Note: "huge.json.txt" is in fact a copy of this file:
		 * 
		 * https://github.com/zeMirco/sf-city-lots-json
		 * 
		 * "City Lots San Francisco in .json
		 * I needed a really big .json file for testing various code.
		 * The CityLots spatial data layer is a representation of the City and County of San Francisco's Subdivision parcels. The initial file is in the .shp (shapefile) format and as the conversion process is quite cumbersome I uploaded the data as a .json file.
		 * Warning: size is 189,9 MB."
		 */

		static void LoopTest(string parserName, Func<string, object> parseFunc, string testFile, int count)
		{
			Console.Clear();
			Console.WriteLine("Parser: {0}", parserName);
			Console.WriteLine();
			Console.WriteLine("Loop Test File: {0}", testFile);
			Console.WriteLine("Iterations: {0}", count.ToString("0,0"));
			Console.WriteLine();
			Console.WriteLine("Press ESC to skip this test or any other key to start...");
			Console.WriteLine();
			if (Console.ReadKey().KeyChar == 27)
				return;

			var json = System.IO.File.ReadAllText(testFile);
			var st = DateTime.Now;
			var l = new List<object>();
			for (var i = 0; i < count; i++)
				l.Add(parseFunc(json));
			var tm = (int)DateTime.Now.Subtract(st).TotalMilliseconds;

			System.Diagnostics.Debug.Assert(l.Count == count);

			Console.WriteLine();
			Console.WriteLine("... Done, in {0} ms. Throughput: {1} characters / seconds.", tm.ToString("0,0"), (1000 * (decimal)(count * json.Length) / (tm > 0 ? tm : 1)).ToString("0,0.00"));
			Console.WriteLine();
			GC.Collect();
			Console.WriteLine("Press a key...");
			Console.WriteLine();
			Console.ReadKey();
		}

		/*  System.Text.Json.JsonParser's figures vs. JSON.NET's, v5.0 r8:

			(on ThinkPad, Intel Core i5 CPU @ 2.40GHz, 4GB RAM (3.80 usable), Win7 64bit)

			"Loop" Test (deserializing x times the JSON contained in the tiny.json.txt file = 91 bytes):
			10,000 iterations: in ~ 110 milliseconds vs. JSON.NET 5.0 r8 in ~ 250 milliseconds
			100,000 iterations: in ~ 850 milliseconds vs. JSON.NET 5.0 r8 in ~ 900 milliseconds
			1,000,000 iterations: in ~ 8 seconds vs. JSON.NET 5.0 r8 in ~ 9 seconds

			"Small" Test (deserializing x times the JSON contained in the small.json.txt file ~ 3.5 kb):
			10,000 iterations: in ~ 2.1 seconds vs. JSON.NET 5.0 r8 in ~ 3.5 seconds
			100,000 iterations: in ~ 22 seconds vs. JSON.NET 5.0 r8... OutOfMemoryException

			Note: fathers.json.txt was generated using: http://experiments.mennovanslooten.nl/2010/mockjson/tryit.html

			"Fathers" Test (12 mb JSON file):
			Parsed in ~ 625 milliseconds vs. JSON.NET 5.0 r8 in ~ 600 milliseconds

			"Huge" Test (180 mb JSON file):
			Parsed in ~ 15.5 seconds vs. JSON.NET 5.0 r8... OutOfMemoryException
		 */
		static void Test(string parserName, Func<string, object> parseFunc, string testFile)
		{
			Console.Clear();
			Console.WriteLine("Parser: {0}", parserName);
			Console.WriteLine();
			Console.WriteLine("Test File: {0}", testFile);
			Console.WriteLine();
			Console.WriteLine("Press ESC to skip this test or any other key to start...");
			Console.WriteLine();
			if (Console.ReadKey().KeyChar == 27)
				return;

			var json = System.IO.File.ReadAllText(testFile);
			var st = DateTime.Now;
			var o = parseFunc(json);
			var tm = (int)DateTime.Now.Subtract(st).TotalMilliseconds;

			Console.WriteLine();
			Console.WriteLine("... Done, in {0} ms. Throughput: {1} characters / seconds.", tm.ToString("0,0"), (1000 * (decimal)json.Length / (tm > 0 ? tm : 1)).ToString("0,0.00"));
			Console.WriteLine();
			if (o is FathersData)
			{
				Console.WriteLine("Fathers : {0}", ((FathersData)o).fathers.Length.ToString("0,0"));
				Console.WriteLine();
			}
			GC.Collect();
			Console.WriteLine("Press a key...");
			Console.WriteLine();
			Console.ReadKey();
		}

		public class Person
		{
			public int Id { get; set; }
			public string Name { get; set; }
			public bool Married { get; set; }
			public string Address { get; set; }
			// Just to be sure we support that one, too:
			public IEnumerable<int> Scores { get; set; }
			public object Data { get; set; }
		}

		public class FathersData
		{
			public Father[] fathers { get; set; }
		}

		public class Father
		{
			public int id { get; set; }
			public string name { get; set; }
			public bool married { get; set; }
			// Lists...
			public List<Son> sons { get; set; }
			// ... or arrays for collections, that's fine:
			public Daughter[] daughters { get; set; }
		}

		public class Son
		{
			public int age { get; set; }
			public string name { get; set; }
		}

		public class Daughter
		{
			public int age { get; set; }
			public string name { get; set; }
		}

		static void SpeedTests()
		{
			LoopTest("JSON.NET 5.0 r8", JsonConvert.DeserializeObject<Person>, TINY_TEST_FILE_PATH, 10000);
			LoopTest(typeof(JsonParser).FullName, new JsonParser().Parse<Person>, TINY_TEST_FILE_PATH, 10000);

			LoopTest("JSON.NET 5.0 r8", JsonConvert.DeserializeObject<Person>, TINY_TEST_FILE_PATH, 100000);
			LoopTest(typeof(JsonParser).FullName, new JsonParser().Parse<Person>, TINY_TEST_FILE_PATH, 100000);

			LoopTest("JSON.NET 5.0 r8", JsonConvert.DeserializeObject<Person>, TINY_TEST_FILE_PATH, 1000000);
			LoopTest(typeof(JsonParser).FullName, new JsonParser().Parse<Person>, TINY_TEST_FILE_PATH, 1000000);

			LoopTest("JSON.NET 5.0 r8", JsonConvert.DeserializeObject, SMALL_TEST_FILE_PATH, 10000);
			LoopTest(typeof(JsonParser).FullName, new JsonParser().Parse<object>, SMALL_TEST_FILE_PATH, 10000);

			LoopTest("JSON.NET 5.0 r8", JsonConvert.DeserializeObject, SMALL_TEST_FILE_PATH, 100000);//(JSON.NET: OutOfMemoryException)
			LoopTest(typeof(JsonParser).FullName, new JsonParser().Parse<object>, SMALL_TEST_FILE_PATH, 100000);

			Test("JSON.NET 5.0 r8", JsonConvert.DeserializeObject<FathersData>, FATHERS_TEST_FILE_PATH);
			Test(typeof(JsonParser).FullName, new JsonParser().Parse<FathersData>, FATHERS_TEST_FILE_PATH);

			Test("JSON.NET 5.0 r8", JsonConvert.DeserializeObject, HUGE_TEST_FILE_PATH);//(JSON.NET: OutOfMemoryException)
			Test(typeof(JsonParser).FullName, new JsonParser().Parse<object>, HUGE_TEST_FILE_PATH);
		}

		static void Main(string[] args)
		{
			SpeedTests();
		}
	}
}