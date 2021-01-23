using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace Artisan.Orm
{
	public class RepositoryBase: IDisposable
	{
		public SqlConnection Connection { get; private set; }

		public string ConnectionString { get; private set; }
		public SqlTransaction Transaction { get; set; }
		
		public RepositoryBase()
		{
			ConnectionString = ConnectionStringHelper.GetConnectionString();

			Connection = new SqlConnection(ConnectionString);

			Transaction = null;
		}
		
		public RepositoryBase(string connectionString, string activeSolutionConfiguration = null)
			: this(null, connectionString, activeSolutionConfiguration) {}

		public RepositoryBase(SqlTransaction transaction, string connectionString, string activeSolutionConfiguration = null)
		{
			if (connectionString.Contains(";") && connectionString.Contains("="))
				ConnectionString = connectionString;
			else
				ConnectionString = ConnectionStringHelper.GetConnectionString(connectionString, activeSolutionConfiguration);

			Connection = new SqlConnection(ConnectionString);

			Transaction = transaction;
		}

		public void BeginTransaction(IsolationLevel isolationLevel, Action<SqlTransaction> action)
		{
			var isConnectionClosed = Connection.State == ConnectionState.Closed;

			if (isConnectionClosed) 
				Connection.Open();

			Transaction = Connection.BeginTransaction(isolationLevel);
			
			try
			{
				action(Transaction);
			}
			catch 
			{
				Transaction.Rollback();
				throw;
			}
			finally
			{
				Transaction?.Dispose();
				Transaction = null;

				if(isConnectionClosed)
					Connection.Close();
			}

		}

		public void BeginTransaction(Action<SqlTransaction> action)
		{
			BeginTransaction(IsolationLevel.Unspecified, action);
		}


		public SqlCommand CreateCommand()
		{
			var cmd = Connection.CreateCommand();

			if (Transaction != null)
				cmd.Transaction = Transaction;

			return cmd;
		}


		public SqlCommand CreateCommand(string sql, params SqlParameter[] sqlParameters)
		{
			var cmd = CreateCommand();

			cmd.ConfigureCommand(sql, sqlParameters);

			return cmd;
		}


		public SqlCommand CreateCommand(string sql, Action<SqlCommand> action)
		{
			var cmd = CreateCommand();

			cmd.ConfigureCommand(sql, action);

			return cmd;
		}


		/// <summary> 
		/// <para/>Prepares SqlCommand and pass it to a Func-parameter.
		/// <para/>Parameter "func" is the code where SqlCommand has to be configured with parameters, execute reader and return result. 
		/// </summary>
		public T GetByCommand<T>(Func<SqlCommand, T> func)
		{
			using (var cmd = CreateCommand())
			{
				return func(cmd);
			}
		}

		/// <summary> 
		/// <para/>Prepares SqlCommand and pass it to a Func-parameter.
		/// <para/>Parameter "func" is the code where SqlCommand has to be configured with parameters, execute reader and return result. 
		/// </summary>
		public async Task<T> GetByCommandAsync<T>(Func<SqlCommand, Task<T>> funcAsync )
		{
			using (var cmd = CreateCommand())
			{
				return await funcAsync(cmd).ConfigureAwait(false);
			}
		}
		

		private static int ExecuteCommand(SqlCommand cmd)
		{
			var returnValueParam = cmd.GetReturnValueParam();
			var isConnectionClosed = true;

			try
			{
				isConnectionClosed = cmd.Connection.State == ConnectionState.Closed;

				if (isConnectionClosed)
					cmd.Connection.Open();

				cmd.ExecuteNonQuery();
			}
			finally
			{
				if (isConnectionClosed)
					cmd.Connection.Close();
			}

			return (int) returnValueParam.Value;
		}


		/// <summary> 
		/// <para/>Executes SqlCommand which returns nothing but ReturnValue.
		/// <para/>Calls ExecuteNonQueryAsync inside.
		/// <para/>Parameter "action" is the code where SqlCommand has to be configured with parameters. 
		/// <para/>Returns ReturnValue - the value from TSQL "RETURN [Value]" statement. If there is no RETURN in TSQL then returns 0.
		/// </summary>
		public Int32 ExecuteCommand (Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand())
			{
				cmd.AddReturnValueParam();

				action(cmd);

				return ExecuteCommand(cmd);
			}
		}

		public Int32 Execute (string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
			{
				cmd.AddReturnValueParam();

				return ExecuteCommand(cmd);
			}
		}

		public Int32 Execute (string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
			{
				cmd.AddReturnValueParam();

				return ExecuteCommand(cmd);
			}
		}
		
		private static async Task<Int32> ExecuteCommandAsync (SqlCommand cmd)
		{
			var returnValueParam = cmd.GetReturnValueParam();
			var isConnectionClosed = true;

			try
			{
				isConnectionClosed = cmd.Connection.State == ConnectionState.Closed;

				if (isConnectionClosed)
					cmd.Connection.Open();

				await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
			}
			finally
			{
				if (isConnectionClosed)
					cmd.Connection.Close();
			}

			return (int) returnValueParam.Value;
		}
		
		/// <summary> 
		/// <para/>Executes SqlCommand which returns nothing but ReturnValue.
		/// <para/>Calls ExecuteNonQueryAsync inside.
		/// <para/>Parameter "action" is the code where SqlCommand has to be configured with parameters. 
		/// <para/>Returns ReturnValue - the value from TSQL "RETURN [Value]" statement. If there is no RETURN in TSQL then returns 0.
		/// </summary>
		public async Task<Int32> ExecuteCommandAsync (Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand())
			{
				cmd.AddReturnValueParam();

				action(cmd);

				return await ExecuteCommandAsync(cmd);
			}
		}
		
		public async Task<Int32> ExecuteAsync (string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
			{
				cmd.AddReturnValueParam();

				return await ExecuteCommandAsync(cmd);
			}
		}

		public async Task<Int32> ExecuteAsync (string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
			{
				cmd.AddReturnValueParam();

				return await ExecuteCommandAsync(cmd);
			}
		}

		/// <summary>
		/// <para>Creates SqlCommand, passes it to Action argument as SqlCommand parameter, returns nothing.</para>
		/// <para>See GitHub Wiki about this method: <a href="https://github.com/lobodava/artisan-orm/wiki/RepositoryBase-methods-for-SqlCommand-initialization#runcommand">https://github.com/lobodava/artisan-orm/wiki/RepositoryBase-methods-for-SqlCommand-initialization#runcommand</a></para>
		/// </summary>
		public void RunCommand(Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand())
				action(cmd);

		}

		public async Task RunCommandAsync(Action<SqlCommand> action)
		{
			await Task.Run(() =>
				{
					using (var cmd = CreateCommand())
						action(cmd);
				}
			).ConfigureAwait(false);
		}

		#region [ ReadTo, ReadAs ]
		
		public T ReadTo<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadTo<T>();
		}

		public T ReadTo<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadTo<T>();
		}
		
		public async Task<T> ReadToAsync<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadToAsync<T>();
		}

		public async Task<T> ReadToAsync<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadToAsync<T>();
		}

		public T ReadAs<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadAs<T>();
		}

		public T ReadAs<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadAs<T>();
		}
		
		public async Task<T> ReadAsAsync<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadAsAsync<T>();
		}

		public async Task<T> ReadAsAsync<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadAsAsync<T>();
		}

		#endregion

		#region [ ReadToList, ReadAsList ]

		public IList<T> ReadToList<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadToList<T>();
		}

		public IList<T> ReadToList<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadToList<T>();
		}

		public async Task<IList<T>> ReadToListAsync<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadToListAsync<T>();
		}

		public async Task<IList<T>> ReadToListAsync<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadToListAsync<T>();
		}

		public IList<T> ReadAsList<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadAsList<T>();
		}

		public IList<T> ReadAsList<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadAsList<T>();
		}
		
		public async Task<IList<T>> ReadAsListAsync<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadAsListAsync<T>();
		}

		public async Task<IList<T>> ReadAsListAsync<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadAsListAsync<T>();
		}
		
		#endregion

		#region [ ReadToObjectRow, ReadAsObjectRow ]

		public ObjectRow ReadToObjectRow<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadToObjectRow<T>();
		}
		
		public ObjectRow ReadToObjectRow<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadToObjectRow<T>();
		}

		public async Task<ObjectRow> ReadToObjectRowAsync<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadToObjectRowAsync<T>();
		}

		public async Task<ObjectRow> ReadToObjectRowAsync<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadToObjectRowAsync<T>();
		}
		
		public ObjectRow ReadAsObjectRow(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadAsObjectRow();
		}

		public ObjectRow ReadAsObjectRow(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadAsObjectRow();
		}
		
		public async Task<ObjectRow> ReadAsObjectRowAsync(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadAsObjectRowAsync();
		}

		public async Task<ObjectRow> ReadAsObjectRowAsync(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadAsObjectRowAsync();
		}

		
		#endregion

		#region [ ReadToObjectRows, ReadAsObjectRows ]

		public ObjectRows ReadToObjectRows<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadToObjectRows<T>();
		}

		public ObjectRows ReadToObjectRows<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadToObjectRows<T>();
		}
		
		public async Task<ObjectRows> ReadToObjectRowsAsync<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadToObjectRowsAsync<T>();
		}

		public async Task<ObjectRows> ReadToObjectRowsAsync<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadToObjectRowsAsync<T>();
		}
		
		public ObjectRows ReadAsObjectRows(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadAsObjectRows();
		}

		public ObjectRows ReadAsObjectRows(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadAsObjectRows();
		}
		
		public async Task<ObjectRows> ReadAsObjectRowsAsync(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadAsObjectRowsAsync();
		}

		public async Task<ObjectRows> ReadAsObjectRowsAsync(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadAsObjectRowsAsync();
		}

		#endregion
		
		#region [ ReadToDictionary ]

		public IDictionary<TKey, TValue> ReadToDictionary<TKey, TValue>(string sql, params SqlParameter[] sqlParameters) 
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadToDictionary<TKey, TValue>();
		}

		public IDictionary<TKey, TValue> ReadToDictionary<TKey, TValue>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadToDictionary<TKey, TValue>();
		}

		public IDictionary<TKey, TValue> ReadAsDictionary<TKey, TValue>(string sql, params SqlParameter[] sqlParameters) 
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadAsDictionary<TKey, TValue>();
		}

		public IDictionary<TKey, TValue> ReadAsDictionary<TKey, TValue>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadAsDictionary<TKey, TValue>();
		}

		public async Task<IDictionary<TKey, TValue>> ReadToDictionaryAsync<TKey, TValue>(string sql, params SqlParameter[] sqlParameters) 
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadToDictionaryAsync<TKey, TValue>();
		}

		public async Task<IDictionary<TKey, TValue>> ReadToDictionaryAsync<TKey, TValue>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadToDictionaryAsync<TKey, TValue>();
		}

		public async Task<IDictionary<TKey, TValue>> ReadAsDictionaryAsync<TKey, TValue>(string sql, params SqlParameter[] sqlParameters) 
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return await cmd.ReadAsDictionaryAsync<TKey, TValue>();
		}

		public async Task<IDictionary<TKey, TValue>> ReadAsDictionaryAsync<TKey, TValue>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return await cmd.ReadAsDictionaryAsync<TKey, TValue>();
		}

		#endregion 

		#region [ ReadToEnumerable, ReadAsEnumerable ]

		public IEnumerable<T> ReadToEnumerable<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadToEnumerable<T>();
		}

		public IEnumerable<T> ReadToEnumerable<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadToEnumerable<T>();
		}

		public IEnumerable<T> ReadAsEnumerable<T>(string sql, params SqlParameter[] sqlParameters)
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadAsEnumerable<T>();
		}

		public IEnumerable<T> ReadAsEnumerable<T>(string sql, Action<SqlCommand> action)
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadAsEnumerable<T>();
		}
		
		#endregion
		
		#region [ ReadToTree, ReadToTreeList ]

		public T ReadToTree<T>(string sql, bool hierarchicallySorted = false, params SqlParameter[] sqlParameters) where T: class, INode<T>
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadToTree<T>(hierarchicallySorted);
		}

		public T ReadToTree<T>(string sql, Action<SqlCommand> action, bool hierarchicallySorted = false) where T: class, INode<T>
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadToTree<T>(hierarchicallySorted);
		}

		public IList<T> ReadToTreeList<T>(string sql, bool hierarchicallySorted = false, params SqlParameter[] sqlParameters) where T: class, INode<T>
		{
			using (var cmd = CreateCommand(sql, sqlParameters))
				return cmd.ReadToTreeList<T>(hierarchicallySorted);
		}

		public IList<T> ReadToTreeList<T>(string sql, Action<SqlCommand> action, bool hierarchicallySorted = false) where T: class, INode<T>
		{
			using (var cmd = CreateCommand(sql, action))
				return cmd.ReadToTreeList<T>(hierarchicallySorted);
		}
		
		#endregion

		public void AddParams(SqlCommand cmd, dynamic parameters)
		{
			var dict = new Dictionary<string, object>();

			foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(parameters))
			{
				object obj = descriptor.GetValue(parameters);
				dict.Add(descriptor.Name, obj);
			}

			cmd.AddParams(dict);
		}

		public static void CheckForDataReplyException(SqlDataReader dr)
		{
			var statusCode = dr.ReadTo<string>(getNextResult: false);

			var dataReplyStatus = DataReply.ParseStatus(statusCode);

			if (dataReplyStatus != null )
			{
				if (dr.NextResult())
					throw new DataReplyException(dataReplyStatus.Value, dr.ReadToArray<DataReplyMessage>());

				throw new DataReplyException(dataReplyStatus.Value);
			}

			dr.NextResult();
		}
		

		public void Dispose()
		{
			Transaction?.Dispose();

			Connection?.Dispose();
		}
	}
}



		