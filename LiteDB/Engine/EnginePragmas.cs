﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// Internal database pragmas persisted inside header page
    /// </summary>
    internal class EnginePragmas
    {
        private const int P_USER_VERSION = 76; // 76-79 (4 bytes)
        private const int P_COLLATION_LCID = 80; // 80-83 (4 bytes)
        private const int P_COLLATION_SORT = 84; // 84-87 (4 bytes)
        private const int P_TIMEOUT = 88; // 88-91 (4 bytes)
        private const int P_LIMIT_SIZE = 92; // 92-95 (4 bytes)
        private const int P_UTC_DATE = 96; // 96-96 (1 byte)
        private const int P_CHECKPOINT = 97; // 97-100 (4 bytes)

        /// <summary>
        /// Interla user version control to detect database changes
        /// </summary>
        public int UserVersion { get; private set; } = 0;

        /// <summary>
        /// Define collation for this database. Value will persisted in disk at first write database. After this, there is no change of collation
        /// </summary>
        public Collation Collation { get; private set; } = Collation.Default;

        /// <summary>
        /// Timeout for waiting unlock operations (default: 1 minute)
        /// </summary>
        public TimeSpan Timeout { get; private set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Max limit of datafile (in bytes) (default: MaxValue)
        /// </summary>
        public long LimitSize { get; private set; } = long.MaxValue;

        /// <summary>
        /// Returns date in UTC timezone from BSON deserialization (default: false == LocalTime)
        /// </summary>
        public bool UtcDate { get; private set; } = false;

        /// <summary>
        /// When LOG file get are bigger than checkpoint size (in pages), do a soft checkpoint (and also do a checkpoint at shutdown)
        /// Checkpoint = 0 means no auto-checkpoint and no shutdown checkpoint
        /// </summary>
        public int Checkpoint { get; private set; } = 1000;

        private readonly Dictionary<string, Pragma> _pragmas;
        private bool _isDirty = false;

        /// <summary>
        /// Get all pragmas
        /// </summary>
        public IEnumerable<Pragma> Pragmas => _pragmas.Values;

        public EnginePragmas()
        {
            _pragmas = new Dictionary<string, Pragma>(StringComparer.OrdinalIgnoreCase)
            {
                ["USER_VERSION"] = new Pragma
                {
                    Name = "USER_VERSION",
                    ReadOnly = false,
                    Get = () => this.UserVersion,
                    Set = (v) => this.UserVersion = v.AsInt32,
                    Read = (b) => this.UserVersion = b.ReadInt32(P_USER_VERSION),
                    Write =  (b) => b.Write(this.UserVersion, P_USER_VERSION)
                },
                ["COLLATION"] = new Pragma
                {
                    Name = "COLLATION",
                    ReadOnly = true,
                    Get = () => this.Collation.ToString(),
                    Set = (v) => this.Collation = new Collation(v.AsString),
                    Read = (b) => this.Collation = new Collation(b.ReadInt32(P_COLLATION_LCID), (CompareOptions)b.ReadInt32(P_COLLATION_SORT)),
                    Write = (b) => 
                    { 
                        b.Write(this.Collation.LCID, P_COLLATION_LCID);
                        b.Write((int)this.Collation.SortOptions, P_COLLATION_SORT);
                    }
                },
                ["TIMEOUT"] = new Pragma
                {
                    Name = "TIMEOUT",
                    ReadOnly = false,
                    Get = () => (int)this.Timeout.TotalSeconds,
                    Set = (v) => this.Timeout = TimeSpan.FromSeconds(v.AsInt32),
                    Read = (b) => this.Timeout = TimeSpan.FromSeconds(b.ReadInt32(P_TIMEOUT)),
                    Write = (b) => b.Write((int)this.Timeout.TotalSeconds, P_TIMEOUT)
                },
                ["LIMIT_SIZE"] = new Pragma
                {
                    Name = "LIMIT_SIZE",
                    ReadOnly = false,
                    Get = () => this.LimitSize,
                    Set = (v) => this.LimitSize = v.AsInt64,
                    Read = (b) => this.LimitSize = b.ReadInt64(P_LIMIT_SIZE),
                    Write = (b) => b.Write(this.LimitSize, P_LIMIT_SIZE)
                },
                ["UTC_DATE"] = new Pragma
                {
                    Name = "UTC_DATE",
                    ReadOnly = false,
                    Get = () => this.UtcDate,
                    Set = (v) => this.UtcDate = v.AsBoolean,
                    Read = (b) => this.UtcDate = b.ReadBool(P_UTC_DATE),
                    Write = (b) => b.Write(this.UtcDate, P_UTC_DATE)
                },
                ["CHECKPOINT"] = new Pragma
                {
                    Name = "CHECKPOINT",
                    ReadOnly = false,
                    Get = () => this.Checkpoint,
                    Set = (v) => this.Checkpoint = v.AsInt32,
                    Read = (b) => this.Checkpoint = b.ReadInt32(P_CHECKPOINT),
                    Write = (b) => b.Write(this.Checkpoint, P_CHECKPOINT)
                }
            };
        }

        public EnginePragmas(BufferSlice buffer)
            : this()
        {
            foreach(var pragma in _pragmas.Values)
            {
                pragma.Read(buffer);
            }
        }

        public void UpdateBuffer(BufferSlice buffer)
        {
            if (_isDirty == false) return;

            foreach(var pragma in _pragmas)
            {
                pragma.Value.Write(buffer);
            }

            _isDirty = false;
        }

        public BsonValue Get(string name)
        {
            if (_pragmas.TryGetValue(name, out var pragma))
            {
                return pragma.Get();
            }

            throw new LiteException(0, $"Pragma `{name}` not exist");
        }

        public void Set(string name, BsonValue value, bool validateReadonly)
        {
            if (_pragmas.TryGetValue(name, out var pragma))
            {
                if (validateReadonly && pragma.ReadOnly == true) throw new LiteException(0, $"Pragma `{pragma.Name}` is read only");

                pragma.Set(value);

                _isDirty = true;
            }
            else
            {
                throw new LiteException(0, $"Pragma `{name}` not exist");
            }
        }
    }
}