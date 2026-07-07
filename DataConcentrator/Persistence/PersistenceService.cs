using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;
using DataConcentrator.Model;
using Microsoft.EntityFrameworkCore;

namespace DataConcentrator.Persistence
{
    public static class PersistenceService
    {
        public static bool Initialize(out string errorMessage)
        {
            errorMessage = null;

            try
            {
                lock (ContextClass.SyncRoot)
                {
                    using (var context = new ContextClass())
                    {
                        context.Database.EnsureCreated();
                    }
                }

                SystemLogger.Log("Application persistence initialized.");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                SystemLogger.LogError("Failed to initialize persistence.", ex);
                return false;
            }
        }

        public static IList<ITag> LoadTags(ITagBuilder builder, out string errorMessage)
        {
            errorMessage = null;
            var tags = new List<ITag>();

            try
            {
                lock (ContextClass.SyncRoot)
                {
                    using (var context = new ContextClass())
                    {
                        var records = context.Tags
                            .Include(tag => tag.Alarms)
                            .OrderBy(tag => tag.Name)
                            .ToList();

                        foreach (var record in records)
                        {
                            var tagType = (Tag_Type)Enum.Parse(typeof(Tag_Type), record.Type);
                            var tag = CreateTag(builder, tagType, record.Address);
                            if (tag == null)
                            {
                                continue;
                            }

                            tag.Name = record.Name;
                            tag.Type = tagType;
                            tag.Address = record.Address;
                            tag.Description = record.Description ?? string.Empty;

                            RestoreAnalogSettings(tag, record);
                            RestoreDigitalSettings(tag, record);

                            tags.Add(tag);
                        }
                    }
                }

                SystemLogger.Log($"Loaded {tags.Count} tags from database.");
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                SystemLogger.LogError("Failed to load tags from database.", ex);
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
                lock (ContextClass.SyncRoot)
                {
                    using (var context = new ContextClass())
                    using (var transaction = context.Database.BeginTransaction())
                    {
                        var lookupName = string.IsNullOrWhiteSpace(oldName) ? tag.Name : oldName;
                        var record = context.Tags
                            .Include(item => item.Alarms)
                            .FirstOrDefault(item => item.Name == lookupName);

                        if (record == null && !string.Equals(lookupName, tag.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            record = context.Tags
                                .Include(item => item.Alarms)
                                .FirstOrDefault(item => item.Name == tag.Name);
                        }

                        if (record == null)
                        {
                            record = new TagEntity();
                        }

                        CopyTagToRecord(tag, record);
                        if (context.Entry(record).State == EntityState.Detached)
                        {
                            context.Tags.Add(record);
                        }
                        context.SaveChanges();

                        if (tag is AnalogInput analogInput && analogInput.AlarmEnabled)
                        {
                            SaveAlarmRecord(context, analogInput);
                        }

                        context.SaveChanges();
                        transaction.Commit();
                    }
                }

                SystemLogger.Log($"Tag saved: {tag.Name}");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                SystemLogger.LogError($"Failed to save tag '{tag.Name}'.", ex);
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
                lock (ContextClass.SyncRoot)
                {
                    using (var context = new ContextClass())
                    using (var transaction = context.Database.BeginTransaction())
                    {
                        var tagRecord = context.Tags
                            .Include(item => item.Alarms)
                            .FirstOrDefault(item => item.Name == tag.Name);

                        if (tagRecord == null)
                        {
                            tagRecord = new TagEntity();
                        }

                        CopyTagToRecord(tag, tagRecord);
                        if (context.Entry(tagRecord).State == EntityState.Detached)
                        {
                            context.Tags.Add(tagRecord);
                        }
                        context.SaveChanges();

                        if (tag.AlarmEnabled)
                        {
                            SaveAlarmRecord(context, tag);
                        }
                        else
                        {
                            var alarm = context.Alarms.FirstOrDefault(item => item.TagName == tag.Name);
                            if (alarm != null)
                            {
                                context.Alarms.Remove(alarm);
                            }
                        }

                        context.SaveChanges();
                        transaction.Commit();
                    }
                }

                SystemLogger.Log($"Alarm saved for tag: {tag.Name}");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                SystemLogger.LogError($"Failed to save alarm for tag '{tag.Name}'.", ex);
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
                lock (ContextClass.SyncRoot)
                {
                    using (var context = new ContextClass())
                    using (var transaction = context.Database.BeginTransaction())
                    {
                        var alarm = FindAlarmDefinition(context, alarmInfo);
                        if (alarm == null)
                        {
                            errorMessage = "Alarm definition was not found.";
                            SystemLogger.Log($"Alarm activation could not be saved for tag '{alarmInfo.TagName}' because no alarm definition exists.");
                            return false;
                        }

                        alarm.State = "Active";
                        alarm.IsAcknowledged = false;

                        var activation = new ActivatedAlarmEntity
                        {
                            Id = GetNextActivatedAlarmId(context),
                            AlarmId = alarm.Id,
                            TagName = alarmInfo.TagName,
                            Message = alarmInfo.Message,
                            Timestamp = alarmInfo.Timestamp
                        };

                        context.ActivatedAlarms.Add(activation);
                        context.SaveChanges();
                        transaction.Commit();

                        alarmInfo.Id = activation.Id;
                        alarmInfo.AlarmDefinitionId = alarm.Id;
                    }
                }

                SystemLogger.Log($"Alarm activated for tag: {alarmInfo.TagName}");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                SystemLogger.LogError($"Failed to save alarm activation for tag '{alarmInfo.TagName}'.", ex);
                return false;
            }
        }

        public static bool TryGetActivatedAlarm(int activatedAlarmId, out AlarmInfo alarmInfo, out string errorMessage)
        {
            alarmInfo = null;
            errorMessage = null;

            try
            {
                lock (ContextClass.SyncRoot)
                {
                    using (var context = new ContextClass())
                    {
                        var activation = context.ActivatedAlarms
                            .FirstOrDefault(item => item.Id == activatedAlarmId);
                        if (activation == null)
                        {
                            return false;
                        }

                        var alarm = context.Alarms.FirstOrDefault(item => item.Id == activation.AlarmId);
                        alarmInfo = new AlarmInfo
                        {
                            Id = activation.Id,
                            AlarmDefinitionId = activation.AlarmId,
                            TagName = activation.TagName,
                            AlarmName = alarm?.Name ?? string.Empty,
                            AlarmType = alarm?.ActivationType ?? string.Empty,
                            Priority = alarm?.Priority ?? 0,
                            LowLimit = alarm?.LowLimit ?? 0,
                            HighLimit = alarm?.HighLimit ?? 0,
                            IsAcknowledged = alarm?.IsAcknowledged ?? false,
                            Message = activation.Message,
                            Timestamp = activation.Timestamp
                        };
                    }
                }

                return alarmInfo != null;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                SystemLogger.LogError($"Failed to read activated alarm '{activatedAlarmId}'.", ex);
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
                lock (ContextClass.SyncRoot)
                {
                    using (var context = new ContextClass())
                    {
                        var record = context.Tags.FirstOrDefault(item => item.Name == tagName);
                        if (record != null)
                        {
                            context.Tags.Remove(record);
                            context.SaveChanges();
                        }
                    }
                }

                SystemLogger.Log($"Tag deleted: {tagName}");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                SystemLogger.LogError($"Failed to delete tag '{tagName}'.", ex);
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
                lock (ContextClass.SyncRoot)
                {
                    using (var context = new ContextClass())
                    {
                        var alarm = context.Alarms.FirstOrDefault(item => item.TagName == tagName);
                        if (alarm != null)
                        {
                            context.Alarms.Remove(alarm);
                            context.SaveChanges();
                        }
                    }
                }

                SystemLogger.Log($"Alarm deleted for tag: {tagName}");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                SystemLogger.LogError($"Failed to delete alarm for tag '{tagName}'.", ex);
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
                lock (ContextClass.SyncRoot)
                {
                    using (var context = new ContextClass())
                    using (var transaction = context.Database.BeginTransaction())
                    {
                        foreach (var tag in tags)
                        {
                            var record = context.Tags
                                .Include(item => item.Alarms)
                                .FirstOrDefault(item => item.Name == tag.Name);

                            if (record == null)
                            {
                                record = new TagEntity();
                            }

                            CopyTagToRecord(tag, record);
                            if (context.Entry(record).State == EntityState.Detached)
                            {
                                context.Tags.Add(record);
                            }
                            context.SaveChanges();

                            if (tag is AnalogInput analogInput)
                            {
                                if (analogInput.AlarmEnabled)
                                {
                                    SaveAlarmRecord(context, analogInput);
                                }
                                else
                                {
                                    var alarm = context.Alarms.FirstOrDefault(item => item.TagName == analogInput.Name);
                                    if (alarm != null)
                                    {
                                        context.Alarms.Remove(alarm);
                                    }
                                }
                            }
                        }

                        context.SaveChanges();
                        transaction.Commit();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = GetInnermostMessage(ex);
                SystemLogger.LogError("Failed to save all tags.", ex);
                return false;
            }
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

        private static void RestoreAnalogSettings(ITag tag, TagEntity record)
        {
            if (tag is IAnalogCommon analogCommon)
            {
                analogCommon.LowLimit = record.LowLimit ?? analogCommon.LowLimit;
                analogCommon.HighLimit = record.HighLimit ?? analogCommon.HighLimit;
                analogCommon.Units = record.Units ?? analogCommon.Units;
            }

            if (tag is AnalogInput analogInput)
            {
                analogInput.ScanTime = record.ScanTime ?? analogInput.ScanTime;
                analogInput.ScanOn = record.ScanOn ?? analogInput.ScanOn;
                analogInput.Deadband = record.Deadband ?? analogInput.Deadband;
                analogInput.Hysteresis = record.Hysteresis ?? analogInput.Hysteresis;
                analogInput.RestoreCurrentValue(record.CurrentValue ?? 0);

                var alarm = record.Alarms.FirstOrDefault();
                if (alarm != null)
                {
                    analogInput.ConfigureAlarm(
                        alarm.LowLimit,
                        alarm.HighLimit,
                        alarm.Message,
                        alarm.Name,
                        alarm.ActivationType,
                        alarm.Priority);
                    analogInput.AlarmDefinitionId = alarm.Id;
                    analogInput.RestoreAlarmState(
                        string.Equals(alarm.State, "Active", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(alarm.State, "Acknowledged", StringComparison.OrdinalIgnoreCase),
                        alarm.IsAcknowledged);
                }
            }
        }

        private static void RestoreDigitalSettings(ITag tag, TagEntity record)
        {
            if (tag is DigitalInput digitalInput && record.CurrentState.HasValue)
            {
                digitalInput.RestoreCurrentState(record.CurrentState.Value);
            }
        }

        private static void CopyTagToRecord(ITag tag, TagEntity record)
        {
            record.Name = tag.Name;
            record.Type = tag.Type.ToString();
            record.Address = tag.Address;
            record.Description = tag.Description ?? string.Empty;

            if (tag is IInputCommon inputCommon)
            {
                record.ScanTime = inputCommon.ScanTime;
                record.ScanOn = inputCommon.ScanOn;
            }

            if (tag is IAnalogCommon analogCommon)
            {
                record.LowLimit = analogCommon.LowLimit;
                record.HighLimit = analogCommon.HighLimit;
                record.Units = analogCommon.Units;
            }

            if (tag is AnalogInput analogInput)
            {
                record.CurrentValue = analogInput.CurrentValue;
                record.Deadband = analogInput.Deadband;
                record.Hysteresis = analogInput.Hysteresis;
            }

            if (tag is DigitalInput digitalInput)
            {
                record.CurrentState = digitalInput.CurrentState;
            }
        }

        private static void SaveAlarmRecord(ContextClass context, AnalogInput tag)
        {
            var alarm = context.Alarms.FirstOrDefault(item => item.TagName == tag.Name);
            if (alarm == null)
            {
                alarm = new AlarmEntity
                {
                    Id = tag.AlarmDefinitionId > 0 ? tag.AlarmDefinitionId : GetNextAlarmId(context),
                    TagName = tag.Name
                };
                context.Alarms.Add(alarm);
            }

            alarm.Name = tag.AlarmName;
            alarm.TagName = tag.Name;
            alarm.ActivationType = string.IsNullOrWhiteSpace(tag.AlarmType) ? "Range" : tag.AlarmType;
            alarm.LowLimit = tag.LowLimit;
            alarm.HighLimit = tag.HighLimit;
            alarm.BoundaryValue = alarm.ActivationType.Equals("Low", StringComparison.OrdinalIgnoreCase)
                || alarm.ActivationType.Equals("Below", StringComparison.OrdinalIgnoreCase)
                    ? tag.LowLimit
                    : tag.HighLimit;
            alarm.Message = tag.AlarmMessage;
            alarm.Priority = tag.AlarmPriority;
            alarm.IsAcknowledged = tag.AlarmAcknowledged;
            alarm.State = tag.AlarmActive
                ? tag.AlarmAcknowledged ? "Acknowledged" : "Active"
                : "Inactive";
            tag.AlarmDefinitionId = alarm.Id;
        }

        private static AlarmEntity FindAlarmDefinition(ContextClass context, AlarmInfo alarmInfo)
        {
            if (alarmInfo.AlarmDefinitionId > 0)
            {
                var byId = context.Alarms.FirstOrDefault(item => item.Id == alarmInfo.AlarmDefinitionId);
                if (byId != null)
                {
                    return byId;
                }
            }

            return context.Alarms
                .OrderBy(item => item.Id)
                .FirstOrDefault(item => item.TagName == alarmInfo.TagName);
        }

        private static int GetNextAlarmId(ContextClass context)
        {
            return context.Alarms.Any() ? context.Alarms.Max(item => item.Id) + 1 : 1;
        }

        private static int GetNextActivatedAlarmId(ContextClass context)
        {
            return context.ActivatedAlarms.Any() ? context.ActivatedAlarms.Max(item => item.Id) + 1 : 1;
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
