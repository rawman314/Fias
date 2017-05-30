using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Fias.Loader.Properties;
using Fias.Loader.SchemaEntities;

namespace Fias.Loader
{
	internal static class Program
	{
		private static readonly int InsertPortion = Settings.Default.InsertPortion;

		private static string[] _fileNames;

		private static SqlConnection GetNewOpenedSqlConnection ()
		{
			var connection = new SqlConnection(Settings.Default.fiasConnectionString);
			connection.Open();
			return connection;
		}

		private static void Main ()
		{
			var startTime = DateTime.Now;
			_fileNames = Directory.GetFiles(Settings.Default.PathToXml);

			// Считываем Schemas.xml
			var schemas = GetSchemasFromXml();

			foreach (var schema in schemas.Where(s => !s.Skip))
			{
				// Получаем полный путь к файлу	
				var dataXmlPath = _fileNames.FirstOrDefault(f => f.ToLower().Contains(string.Format("as_{0}_", schema.Name)));
				if (dataXmlPath == null)
				{
					Console.WriteLine("Xml-file for {0} not found!", schema.Name);
					continue;
				}

				Console.WriteLine("Import from [{0}] started!", schema.Name);
				// Начинаем считывать
				using (XmlReader reader = new XmlTextReader(dataXmlPath))
				{
					PrepareTable(schema);
					ImportData(reader, schema);
				}
			}
			Console.WriteLine("Time: {0}", DateTime.Now.Subtract(startTime).TotalSeconds);
			Console.Write("Press any key to exit...");
			Console.ReadKey();
		}

		#region Privitive SQL-queries

		private static bool IsTableExist (string tableName)
		{
			bool isExist;
			using (var sqlConnection = GetNewOpenedSqlConnection())
			{
				var command = new SqlCommand(@"SELECT Name FROM dbo.sysobjects WHERE (xtype = 'U')  AND (Name = @tableName)",
											 sqlConnection);
				command.Parameters.AddWithValue("tableName", tableName);
				var result = command.ExecuteReader();
				isExist = result.HasRows;
				sqlConnection.Close();
			}
			return isExist;
		}

		private static int GetRecordCount (Schema schema)
		{
			object result;
			using (var sqlConnection = GetNewOpenedSqlConnection())
			{
				var getCountQuery = string.Format(@"SELECT row_count
				FROM sys.dm_db_partition_stats
				WHERE object_name(object_id) = 'as_{0}'", schema.Name);

				var command = new SqlCommand(getCountQuery, sqlConnection);
				result = command.ExecuteScalar();
				sqlConnection.Close();
			}
			return Convert.ToInt32(result);
		}

		private static void DeleteTable (string tableName)
		{
			using (var sqlConnection = GetNewOpenedSqlConnection())
			{
				var clearQuery = new SqlCommand(string.Format("DROP TABLE [{0}]", tableName), sqlConnection);
				clearQuery.ExecuteNonQuery();
				sqlConnection.Close();
			}
		}

		private static string GenerateSqlForCreateTable (Schema schema)
		{
			var tableName = "as_" + schema.Name;

			var fieldConfigs = new List<string>();
			foreach (var field in schema.Fields)
			{
				var type = string.Format(field.Type.Sql, field.MaxLenght > 0 ? field.MaxLenght.ToString() : "MAX");
				var constrain = field.IsPrimary ? "NOT NULL" : "NULL";
				fieldConfigs.Add(string.Format(@"[{0}] {1} {2}", field.Name.ToUpper(), type, constrain));
			}
			var primaryKeySql = string.Empty;
			if (schema.Fields.Any(f => f.IsPrimary))
			{
				var fieldName = schema.Fields.First(f => f.IsPrimary).Name.ToUpper();
				primaryKeySql = string.Format(",\n CONSTRAINT [{0}_pk] PRIMARY KEY NONCLUSTERED ([{1}])", tableName, fieldName);
			}

			var createQuerySql = string.Format(@"CREATE TABLE [dbo].[{0}] ({1}{2})",
											   tableName,
											   string.Join(",\n", fieldConfigs),
											   primaryKeySql);
			return createQuerySql;
		}

		private static void CreateTable (Schema schema)
		{
			using (var sqlConnection = GetNewOpenedSqlConnection())
			{
				var createQuerySql = GenerateSqlForCreateTable(schema);
				var createTableQuery = new SqlCommand(createQuerySql, sqlConnection);
				createTableQuery.ExecuteNonQuery();
				sqlConnection.Close();
			}
		}

		#endregion

		private static IEnumerable<Schema> GetSchemasFromXml ()
		{
			var serializer = new XmlSerializer(typeof(SchemaCollection));

			var reader = new StreamReader("Schemas.xml");
			var schemas = (SchemaCollection) serializer.Deserialize(reader);
			reader.Close();

			return schemas.Schemas;
		}

		private static void PrepareTable (Schema schema)
		{
			var tableName = "as_" + schema.Name;
			var isExist = IsTableExist(tableName);
			bool skipCreate = false;

			// Если таблица существует - удаляем (на случай если изменилась структура)
			if (isExist)
			{
				var rowsCount = GetRecordCount(schema);
				// Если в очень большой таблице уже занесены данные, то мы просто продолжим, удалять её не будем
				if (rowsCount > 0 && schema.Big)
				{
					schema.Filled = rowsCount;
					skipCreate = true;
				}
				else
				// Маленькие таблицы просто пересоздаем
				{
					DeleteTable(tableName);
				}
			}

			if (skipCreate)
			{
				return;
			}

			// Создаем таблицу по схеме
			CreateTable(schema);
		}

		private static void ImportData (XmlReader reader, Schema schema)
		{
			var count = 0;
			var fieldCount = schema.Fields.Count;
			var rowSql = new List<string>();
			if (schema.Filled > 0)
			{
				Console.WriteLine("Skip first {0} records.", schema.Filled);
			}
			while (reader.Read())
			{
				if (schema.Limit > 0 && count >= schema.Limit)
				{
					break;
				}

				if (reader.Name == schema.XmlElement)
				{
					count++;
					if (schema.Big && schema.Filled > 0 && schema.Filled >= count)
					{
						if (count % InsertPortion == 0)
						{
							Console.WriteLine("Skiped {0} record", count);
						}
						continue;
					}
					var values = new List<string>();
					for (int i = 0; i < fieldCount; i++)
					{
						var field = schema.Fields[i];
						var value = reader.GetAttribute(field.Name.ToUpper());
						values.Add(WrapValue(field, value));
					}
					rowSql.Add(string.Format("({0})", string.Join(",", values)));

					if (count % InsertPortion == 0)
					{
						ExecuteInserts(schema, count, rowSql, fieldCount);
					}
				}
			}
			// Последняя порция данных
			if (rowSql.Any())
			{
				ExecuteInserts(schema, count, rowSql, fieldCount);
			}
		}

		private static string WrapValue (Field field, string value)
		{
			var wrappedValue = string.IsNullOrWhiteSpace(value) || value == "1900-01-01"
								? "null"
								: string.Format(field.Type.Format, value.Replace("'", "''"));
			return wrappedValue;
		}

		private static void ExecuteInserts (Schema schema, int count, List<string> rowSql, int fieldCount)
		{
			var insertSql = new StringBuilder(string.Format("INSERT INTO [as_{0}] (", schema.Name));

			for (var i = 0; i < fieldCount; i++)
			{
				insertSql.AppendFormat(fieldCount - 1 == i ? "[{0}]" : "[{0}],", schema.Fields[i].Name.ToUpper());
			}
			insertSql.AppendFormat(")\nVALUES\n{0};", string.Join(",\n", rowSql));

			using (var sqlConnection = GetNewOpenedSqlConnection())
			{
				var sql = new SqlCommand(insertSql.ToString(), sqlConnection);
				sql.ExecuteNonQuery();
				sqlConnection.Close();
			}
			rowSql.Clear();
			Console.WriteLine("Submit {0}", count);
		}
	}
}