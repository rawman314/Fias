namespace Fias.Loader.SchemaEntities
{
	public class TypeConfig
	{
		public string Name;
		public string Sql;
		public string Format;

		public TypeConfig (string name, string sql, string format)
		{
			Name = name;
			Sql = sql;
			Format = format;
		}
	}
}