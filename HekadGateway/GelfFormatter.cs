using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog.Events;
using Serilog.Formatting;

namespace HekadGateway
{
	public sealed class GelfFormatter : ITextFormatter
	{
		readonly string _instanceId;
		readonly string _deploymentName;
		const string GelfVersion = "1.0";
		const int ShortMessageMaxLength = 250;
		readonly string _host = Dns.GetHostName();
		static readonly DateTime LinuxEpoch = new DateTime(1970, 1, 1);

		public GelfFormatter(string instanceId, string deploymentName)
		{
			_instanceId = instanceId;
			_deploymentName = deploymentName;
		}

		static double DateTimeToUnixTimestamp(DateTimeOffset dateTime)
		{
			return (dateTime.ToUniversalTime() - LinuxEpoch).TotalSeconds;
		}

		public void Format(LogEvent e, TextWriter output)
		{
			var renderMessage = e.RenderMessage(CultureInfo.InvariantCulture);
			var shortMessage = renderMessage;
			if (shortMessage.Length > ShortMessageMaxLength)
			{
				shortMessage = shortMessage.Substring(0, ShortMessageMaxLength);
			}
			string facility = "GELF";

			LogEventPropertyValue value;
			if (e.Properties.TryGetValue("SourceContext", out value))
			{
				facility = value.ToString();
			}

			var message = new GelfMessage
				{
					Version = GelfVersion,
					Host = _host,
					Timestamp = DateTimeToUnixTimestamp(e.Timestamp),
					Level = GetSeverityLevel(e.Level),
					Facility = facility,
					ShortMessage = shortMessage,
					FullMessage = renderMessage,
					Logger = facility,
				};

			var json = JObject.FromObject(message);


			//We will persist them "Additional Fields" according to Gelf spec
			foreach (var property in e.Properties)
			{
				AddAdditionalField(json, property.Key, property.Value);

			}
			if (e.Exception != null)
			{
				AddAdditionalField(json, "ExceptionSource", e.Exception.Source);
				AddAdditionalField(json, "ExceptionMessage", e.Exception.Message);
				AddAdditionalField(json, "StackTrace", e.Exception.StackTrace);
			}
			AddAdditionalField(json, "Instance", _instanceId);
			AddAdditionalField(json, "Deployment", _deploymentName);
			if (json == null) return;
			var jsonString = json.ToString(Formatting.None, null);
			output.WriteLine(jsonString);
		}

		/// <summary>
		/// Values from SyslogSeverity enum here: http://marc.info/?l=log4net-dev&m=109519564630799
		/// </summary>
		/// <param name="level"></param>
		/// <returns></returns>
		static int GetSeverityLevel(LogEventLevel level)
		{
			switch (level)
			{
				case LogEventLevel.Verbose:
					return 7;
				case LogEventLevel.Debug:
					return 7;
				case LogEventLevel.Information:
					return 6;
				case LogEventLevel.Warning:
					return 4;
				case LogEventLevel.Error:
					return 3;
				case LogEventLevel.Fatal:
					return 2;
				default:
					throw new ArgumentOutOfRangeException("level");
			}
		}

		static void AddAdditionalField(IDictionary<string, JToken> jObject, string key, object value)
		{
			if (key == null) return;

			//According to the GELF spec, libraries should NOT allow to send id as additional field (_id)
			//Server MUST skip the field because it could override the MongoDB _key field
			if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
				key = "id_";

			//According to the GELF spec, additional field keys should start with '_' to avoid collision
			if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase))
				key = "_" + key;

			jObject.Add(key, value.ToString());
		}
	}

	[JsonObject(MemberSerialization.OptIn)]
	public class GelfMessage
	{
		[JsonProperty("facility")]
		public string Facility { get; set; }

		[JsonProperty("file")]
		public string File { get; set; }

		[JsonProperty("full_message")]
		public string FullMessage { get; set; }

		[JsonProperty("host")]
		public string Host { get; set; }

		[JsonProperty("level")]
		public int Level { get; set; }

		[JsonProperty("line")]
		public string Line { get; set; }

		[JsonProperty("short_message")]
		public string ShortMessage { get; set; }

		[JsonProperty("_timestamp")]
		public double Timestamp { get; set; }

		[JsonProperty("logger")]
		public string Logger { get; set; }

		[JsonProperty("version")]
		public string Version { get; set; }
	}

}