using System.Collections.Generic;
using System.Xml.Serialization;

namespace Fias.Loader.SchemaEntities
{
	[XmlRoot("root")]
	public class SchemaCollection
	{
		[XmlArray("schemas")]
		[XmlArrayItem("schema")]
		public List<Schema> Schemas { get; set; }
	}
}