using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Contracts;
using DataConcentrator.Model;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace DataConcentrator.Persistence
{
    public static class PersistenceService
    {
        private static readonly object syncRoot = new object();
        private static bool sqliteInitialized;

        private static string DatabasePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scada_data.db");

        private static string ConnectionString =>
            new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();

        public static bool Initialize(out string errorMessage)
        {
            errorMessage = null;

            try
            {
                lock (syncRoot)
                {
                    EnsureSqliteInitialized();
                    using (var connection = OpenConnection())
                    {
                        ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS TagRecords (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    Type INTEGER NOT NULL,
    Address TEXT NULL,
    Description TEXT NULL,
    CurrentValue REAL NULL,
    CurrentState INTEGER NULL,
    ScanTime REAL NULL,
    ScanOn INTEGER NULL,
    LowLimit REAL NULL,
    HighLimit REAL NULL,
    Units TEXT NULL,
    Deadband REAL NULL,
    Hysteresis REAL NULL
);");

                        ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS AlarmConfigurationRecords (
    TagRecordId INTEGER NOT NULL PRIMARY KEY,
    AlarmName TEXT NULL,
    AlarmType TEXT NULL,
    Priority INTEGER NOT NULL,
    LowLimit REAL NOT NULL,
    HighLimit REAL NOT NULL,
    Message TEXT NULL,
    IsAcknowledged INTEGER NOT NULL,
    IsActive INTEGER NOT NULL,
    FOREIGN KEY(TagRecordId) REFERENCES TagRecords(Id) ON DELETE CASCADE
);");

                        ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS AlarmRecords (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TagName TEXT NULL,
    Address TEXT NULL,
    TriggeredValue REAL NOT NULL,
    LowLimit REAL NOT NULL,
    HighLimit REAL NOT NULL,
    Message TEXT NULL,
    Timestamp TEXT NOT NULL
);");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                return false;
            }
        }

        public static IList<ITag> LoadTags(ITagBuilder builder, out string errorMessage)
        {
            errorMessage = null;
            var tags = new List<ITag>();

            try
            {
                lock (syncRoot)
                {
                    using (var connection = OpenConnection())
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
SELECT
    t.Id, t.Name, t.Type, t.Address, t.Description, t.CurrentValue, t.CurrentState,
    t.ScanTime, t.ScanOn, t.LowLimit, t.HighLimit, t.Units, t.Deadband, t.Hysteresis,
    a.AlarmName, a.AlarmType, a.Priority, a.LowLimit AS AlarmLowLimit,
    a.HighLimit AS AlarmHighLimit, a.Message, a.IsAcknowledged, a.IsActive
FROM TagRecords t
LEFT JOIN AlarmConfigurationRecords a ON a.TagRecordId = t.Id
ORDER BY t.Id;";

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var tagType = (Tag_Type)reader.GetInt32(reader.GetOrdinal("Type"));
                                var tag = CreateTag(builder, tagType, GetString(reader, "Address"));
                                if (tag == null)
                                {
                                    continue;
                                }

                                tag.Name = GetString(reader, "Name");
                                tag.Type = tagType;
                                tag.Address = GetString(reader, "Address");
                                tag.Description = GetString(reader, "Description") ?? string.Empty;

                                RestoreAnalogSettings(tag, reader);
                                RestoreDigitalSettings(tag, reader);
                                tags.Add(tag);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
            }

            return tags;
        }

        public static bool SaveTag(ITag tag, out string errorMessage, string oldName = null)
        {
            errorMessage = null;

            if (tag == null)
            {
                return false;
            }

            try
            {
                lock (syncRoot)
                {
                    using (var connection = OpenConnection())
                    using (var transaction = connection.BeginTransaction())
                    {
                        var lookupName = string.IsNullOrWhiteSpace(oldName) ? tag.Name : oldName;
                        var tagId = GetTagId(connection, transaction, lookupName);
                        if (!tagId.HasValue && !string.Equals(lookupName, tag.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            tagId = GetTagId(connection, transaction, tag.Name);
                        }

                        SaveTagRecord(connection, transaction, tag, tagId);
                        if (tag is AnalogInput analogInput && analogInput.AlarmEnabled)
                        {
                            SaveAlarmRecord(connection, transaction, analogInput);
                        }

                        transaction.Commit();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                return false;
            }
        }

        public static bool SaveAlarm(AnalogInput tag, out string errorMessage)
        {
            errorMessage = null;

            if (tag == null)
            {
                return false;
            }

            try
            {
                lock (syncRoot)
                {
                    using (var connection = OpenConnection())
                    using (var transaction = connection.BeginTransaction())
                    {
                        SaveTagRecord(connection, transaction, tag, GetTagId(connection, transaction, tag.Name));
                        if (tag.AlarmEnabled)
                        {
                            SaveAlarmRecord(connection, transaction, tag);
                        }
                        else
                        {
                            DeleteAlarmRecord(connection, transaction, tag.Name);
                        }

                        transaction.Commit();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                return false;
            }
        }

        public static bool SaveAlarmEvent(AlarmInfo alarmInfo, out string errorMessage)
        {
            errorMessage = null;

            if (alarmInfo == null)
            {
                return false;
            }

            try
            {
                lock (syncRoot)
                {
                    using (var connection = OpenConnection())
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
INSERT INTO AlarmRecords (TagName, Address, TriggeredValue, LowLimit, HighLimit, Message, Timestamp)
VALUES ($tagName, $address, $triggeredValue, $lowLimit, $highLimit, $message, $timestamp);
SELECT last_insert_rowid();";
                        AddParameter(command, "$tagName", alarmInfo.TagName);
                        AddParameter(command, "$address", alarmInfo.Address);
                        AddParameter(command, "$triggeredValue", alarmInfo.TriggeredValue);
                        AddParameter(command, "$lowLimit", alarmInfo.LowLimit);
                        AddParameter(command, "$highLimit", alarmInfo.HighLimit);
                        AddParameter(command, "$message", alarmInfo.Message);
                        AddParameter(command, "$timestamp", alarmInfo.Timestamp.ToString("O"));

                        alarmInfo.Id = Convert.ToInt32((long)command.ExecuteScalar());
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                return false;
            }
        }

        public static bool DeleteTag(string tagName, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return false;
            }

            try
            {
                lock (syncRoot)
                {
                    using (var connection = OpenConnection())
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "DELETE FROM TagRecords WHERE Name = $name;";
                        AddParameter(command, "$name", tagName);
                        command.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                return false;
            }
        }

        public static bool DeleteAlarm(string tagName, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return false;
            }

            try
            {
                lock (syncRoot)
                {
                    using (var connection = OpenConnection())
                    using (var transaction = connection.BeginTransaction())
                    {
                        DeleteAlarmRecord(connection, transaction, tagName);
                        transaction.Commit();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                return false;
            }
        }

        public static bool SaveAll(IEnumerable<ITag> tags, out string errorMessage)
        {
            errorMessage = null;

            if (tags == null)
            {
                return false;
            }

            try
            {
                lock (syncRoot)
                {
                    using (var connection = OpenConnection())
                    using (var transaction = connection.BeginTransaction())
                    {
                        foreach (var tag in tags)
                        {
                            SaveTagRecord(connection, transaction, tag, GetTagId(connection, transaction, tag.Name));

                            if (tag is AnalogInput analogInput)
                            {
                                if (analogInput.AlarmEnabled)
                                {
                                    SaveAlarmRecord(connection, transaction, analogInput);
                                }
                                else
                                {
                                    DeleteAlarmRecord(connection, transaction, analogInput.Name);
                                }
                            }
                        }

                        transaction.Commit();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                return false;
            }
        }

        private static void EnsureSqliteInitialized()
        {
            if (sqliteInitialized)
            {
                return;
            }

            Batteries.Init();
            sqliteInitialized = true;
        }

        private static SqliteConnection OpenConnection()
        {
            EnsureSqliteInitialized();
            var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
            return connection;
        }

        private static ITag CreateTag(ITagBuilder builder, Tag_Type type, string address)
        {
            switch (type)
            {
                case Tag_Type.AI:
                    return builder.CreateAnalogInput(address);
                case Tag_Type.AO:
                    return builder.CreateAnalogOutput(address);
                case Tag_Type.DI:
                    return builder.CreateDigitalInput(address);
                case Tag_Type.DO:
                    return builder.CreateDigitalOutput(address);
                default:
                    return null;
            }
        }

        private static void RestoreAnalogSettings(ITag tag, IDataRecord record)
        {
            if (tag is IAnalogCommon analogCommon)
            {
                analogCommon.LowLimit = GetDouble(record, "LowLimit") ?? analogCommon.LowLimit;
                analogCommon.HighLimit = GetDouble(record, "HighLimit") ?? analogCommon.HighLimit;
                analogCommon.Units = GetString(record, "Units") ?? analogCommon.Units;
            }

            if (tag is AnalogInput analogInput)
            {
                analogInput.ScanTime = GetDouble(record, "ScanTime") ?? analogInput.ScanTime;
                analogInput.ScanOn = GetBool(record, "ScanOn") ?? analogInput.ScanOn;
                analogInput.Deadband = GetDouble(record, "Deadband") ?? analogInput.Deadband;
                analogInput.Hysteresis = GetDouble(record, "Hysteresis") ?? analogInput.Hysteresis;
                analogInput.RestoreCurrentValue(GetDouble(record, "CurrentValue") ?? 0);

                var alarmName = GetString(record, "AlarmName");
                if (!string.IsNullOrWhiteSpace(alarmName))
                {
                    analogInput.ConfigureAlarm(
                        GetDouble(record, "AlarmLowLimit") ?? analogInput.LowLimit,
                        GetDouble(record, "AlarmHighLimit") ?? analogInput.HighLimit,
                        GetString(record, "Message"),
                        alarmName,
                        GetString(record, "AlarmType"),
                        GetInt(record, "Priority") ?? 0);
                    analogInput.RestoreAlarmState(
                        GetBool(record, "IsActive").GetValueOrDefault(),
                        GetBool(record, "IsAcknowledged").GetValueOrDefault());
                }
            }
        }

        private static void RestoreDigitalSettings(ITag tag, IDataRecord record)
        {
            if (tag is DigitalInput digitalInput)
            {
                var currentState = GetBool(record, "CurrentState");
                if (currentState.HasValue)
                {
                    digitalInput.RestoreCurrentState(currentState.Value);
                }
            }
        }

        private static int SaveTagRecord(SqliteConnection connection, SqliteTransaction transaction, ITag tag, int? existingTagId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                if (existingTagId.HasValue)
                {
                    command.CommandText = @"
UPDATE TagRecords
SET Name = $name, Type = $type, Address = $address, Description = $description,
    CurrentValue = $currentValue, CurrentState = $currentState, ScanTime = $scanTime,
    ScanOn = $scanOn, LowLimit = $lowLimit, HighLimit = $highLimit, Units = $units,
    Deadband = $deadband, Hysteresis = $hysteresis
WHERE Id = $id;";
                    AddParameter(command, "$id", existingTagId.Value);
                }
                else
                {
                    command.CommandText = @"
INSERT INTO TagRecords
    (Name, Type, Address, Description, CurrentValue, CurrentState, ScanTime,
     ScanOn, LowLimit, HighLimit, Units, Deadband, Hysteresis)
VALUES
    ($name, $type, $address, $description, $currentValue, $currentState, $scanTime,
     $scanOn, $lowLimit, $highLimit, $units, $deadband, $hysteresis);";
                }

                AddTagParameters(command, tag);
                command.ExecuteNonQuery();
            }

            return existingTagId ?? GetTagId(connection, transaction, tag.Name).Value;
        }

        private static void SaveAlarmRecord(SqliteConnection connection, SqliteTransaction transaction, AnalogInput tag)
        {
            var tagId = GetTagId(connection, transaction, tag.Name);
            if (!tagId.HasValue)
            {
                tagId = SaveTagRecord(connection, transaction, tag, null);
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO AlarmConfigurationRecords
    (TagRecordId, AlarmName, AlarmType, Priority, LowLimit, HighLimit, Message, IsAcknowledged, IsActive)
VALUES
    ($tagRecordId, $alarmName, $alarmType, $priority, $lowLimit, $highLimit, $message, $isAcknowledged, $isActive)
ON CONFLICT(TagRecordId) DO UPDATE SET
    AlarmName = excluded.AlarmName,
    AlarmType = excluded.AlarmType,
    Priority = excluded.Priority,
    LowLimit = excluded.LowLimit,
    HighLimit = excluded.HighLimit,
    Message = excluded.Message,
    IsAcknowledged = excluded.IsAcknowledged,
    IsActive = excluded.IsActive;";
                AddParameter(command, "$tagRecordId", tagId.Value);
                AddParameter(command, "$alarmName", tag.AlarmName);
                AddParameter(command, "$alarmType", tag.AlarmType);
                AddParameter(command, "$priority", tag.AlarmPriority);
                AddParameter(command, "$lowLimit", tag.LowLimit);
                AddParameter(command, "$highLimit", tag.HighLimit);
                AddParameter(command, "$message", tag.AlarmMessage);
                AddParameter(command, "$isAcknowledged", tag.AlarmAcknowledged);
                AddParameter(command, "$isActive", tag.AlarmActive);
                command.ExecuteNonQuery();
            }
        }

        private static void DeleteAlarmRecord(SqliteConnection connection, SqliteTransaction transaction, string tagName)
        {
            var tagId = GetTagId(connection, transaction, tagName);
            if (!tagId.HasValue)
            {
                return;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM AlarmConfigurationRecords WHERE TagRecordId = $tagRecordId;";
                AddParameter(command, "$tagRecordId", tagId.Value);
                command.ExecuteNonQuery();
            }
        }

        private static int? GetTagId(SqliteConnection connection, SqliteTransaction transaction, string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT Id FROM TagRecords WHERE Name = $name;";
                AddParameter(command, "$name", tagName);
                var result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? (int?)null : Convert.ToInt32(result);
            }
        }

        private static void AddTagParameters(SqliteCommand command, ITag tag)
        {
            AddParameter(command, "$name", tag.Name);
            AddParameter(command, "$type", (int)tag.Type);
            AddParameter(command, "$address", tag.Address);
            AddParameter(command, "$description", tag.Description);

            AddParameter(command, "$currentValue", tag is AnalogInput analogInput ? (object)analogInput.CurrentValue : DBNull.Value);
            AddParameter(command, "$currentState", tag is DigitalInput digitalInput ? (object)digitalInput.CurrentState : DBNull.Value);
            AddParameter(command, "$scanTime", tag is IInputCommon input ? (object)input.ScanTime : DBNull.Value);
            AddParameter(command, "$scanOn", tag is IInputCommon inputCommon ? (object)inputCommon.ScanOn : DBNull.Value);
            AddParameter(command, "$lowLimit", tag is IAnalogCommon analog ? (object)analog.LowLimit : DBNull.Value);
            AddParameter(command, "$highLimit", tag is IAnalogCommon analogCommon ? (object)analogCommon.HighLimit : DBNull.Value);
            AddParameter(command, "$units", tag is IAnalogCommon analogUnits ? (object)analogUnits.Units : DBNull.Value);
            AddParameter(command, "$deadband", tag is AnalogInput deadbandInput ? (object)deadbandInput.Deadband : DBNull.Value);
            AddParameter(command, "$hysteresis", tag is AnalogInput hysteresisInput ? (object)hysteresisInput.Hysteresis : DBNull.Value);
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = commandText;
                command.ExecuteNonQuery();
            }
        }

        private static void AddParameter(SqliteCommand command, string name, object value)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        private static string GetString(IDataRecord record, string name)
        {
            var ordinal = record.GetOrdinal(name);
            return record.IsDBNull(ordinal) ? null : record.GetString(ordinal);
        }

        private static double? GetDouble(IDataRecord record, string name)
        {
            var ordinal = record.GetOrdinal(name);
            return record.IsDBNull(ordinal) ? (double?)null : Convert.ToDouble(record.GetValue(ordinal));
        }

        private static int? GetInt(IDataRecord record, string name)
        {
            var ordinal = record.GetOrdinal(name);
            return record.IsDBNull(ordinal) ? (int?)null : Convert.ToInt32(record.GetValue(ordinal));
        }

        private static bool? GetBool(IDataRecord record, string name)
        {
            var ordinal = record.GetOrdinal(name);
            return record.IsDBNull(ordinal) ? (bool?)null : Convert.ToInt32(record.GetValue(ordinal)) != 0;
        }

        private static string GetInnermostMessage(Exception ex)
        {
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            return ex.Message;
        }
    }
}
