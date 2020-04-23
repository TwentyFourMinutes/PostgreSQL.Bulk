// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Wrong Usage", "DF0010:Marks undisposed local variables.", Justification = "This variable always gets diposed in the called CompleteAndWriteRelationValues method.", Scope = "member", Target = "~M:PostgreSQL.Bulk.NpgsqlConnectionExtensions.PerformCopy``1(Npgsql.NpgsqlConnection,PostgreSQL.Bulk.EntityDefinition{``0},System.Collections.Generic.IEnumerable{``0},System.String,System.Threading.CancellationToken)~System.Threading.Tasks.Task{System.UInt64}")]
