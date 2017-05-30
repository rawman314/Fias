using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Fias.Loader.SchemaEntities
{
	public class Field
	{
		private static Dictionary<string, TypeConfig> Map = 
			new Dictionary<string, TypeConfig>
				{
					{"string",new TypeConfig("string", "nvarchar({0}) COLLATE Cyrillic_General_CI_AS", "N'{0}'")},
					{"int",new TypeConfig("int", "int", "{0}")},
					{"date", new TypeConfig("date", "datetime", "'{0}'")},
					{"guid",new TypeConfig("guid", "uniqueidentifier", "'{0}'")},
					{"bool",new TypeConfig("bool", "bit", "{0}")},
				};

		[XmlAttribute("name")]
		public string Name { get; set; }

		[XmlAttribute("type")]
		public string TypeName { get; set; }
		private TypeConfig _type;
		public TypeConfig Type { get { return _type ?? ( _type = Map[TypeName] ); } }

		[XmlAttribute("primary")]
		public bool IsPrimary;

		[XmlAttribute("max")]
		public int MaxLenght { get; set; }
	}
}