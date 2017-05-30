using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Fias.Loader.SchemaEntities;

namespace Fias.Loader
{
	[XmlRoot("schemas")]
	public class Schema
	{
		[XmlAttribute("name")]
		public string Name { get; set; }

		[XmlAttribute("xmlElement")]
		public string XmlElement { get; set; }

		[XmlAttribute("skip")]
		public bool Skip { get; set; }

		[XmlAttribute("limit")]
		public int Limit { get; set; }

		[XmlAttribute("big")]
		public bool Big { get; set; }

		public int Filled;

		[XmlElement("field", typeof(Field))]
		public List<Field> Fields { get; set; }
	}
}