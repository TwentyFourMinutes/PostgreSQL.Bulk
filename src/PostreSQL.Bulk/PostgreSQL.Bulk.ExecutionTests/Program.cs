using Dapper;
using Npgsql;
using PostgreSQLCopyHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PostgreSQL.Bulk.ExecutionTests
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var sw = Stopwatch.StartNew();
            EntityConfigurator.BuildConfigurations();

            sw.Stop();

            Console.WriteLine(sw.Elapsed.TotalMilliseconds);

            sw = Stopwatch.StartNew();

            var test2 = new PostgreSQLCopyHelper<Person>("Persons").MapUUID("Id", x => x.Id).MapText("Name", x => x.Name).UsePostgresQuoting();
            var test3 = new PostgreSQLCopyHelper<Email>("Emails").MapUUID("Id", x => x.Id).MapText("Address", x => x.Address).MapUUID("PersonId", x => x.PersonId).UsePostgresQuoting();

            sw.Stop();

            Console.WriteLine(sw.Elapsed.TotalMilliseconds);

            var test = new Person[1000000];

            for (int i = 0; i < 1000000; i++)
            {
                test[i] = new Person()
                {
                    Name = i.ToString(),
                    Emails = new List<Email>() { new Email { Address = i.ToString() } }
                };
            }

            using (var connection = new NpgsqlConnection("Server=127.0.0.1;Port=5432;Database=testing;UserId=postgres;Password=ybVolVw%9@dz"))
            {
                await connection.ExecuteAsync("TRUNCATE \"Persons\" CASCADE");

                await connection.OpenAsync();

                sw = Stopwatch.StartNew();

                await connection.BulkInsertAsync(test);

                sw.Stop();
            }

            Console.WriteLine(sw.Elapsed.TotalMilliseconds);

            //using (var connection = new NpgsqlConnection("Server=127.0.0.1;Port=5432;Database=testing;UserId=postgres;Password=ybVolVw%9@dz"))
            //{
            //    await connection.ExecuteAsync("TRUNCATE \"Persons\" CASCADE");

            //    await connection.OpenAsync();

            //    sw = Stopwatch.StartNew();

            //    await test2.SaveAllAsync(connection, test.Select(x => { x.Id = Guid.NewGuid(); return x; }));
            //    await test3.SaveAllAsync(connection, test.SelectMany(x => x.Emails.Select(y => { y.Id = Guid.NewGuid(); y.PersonId = x.Id; return y; })));

            //    sw.Stop();
            //}

            Console.WriteLine(sw.Elapsed.TotalMilliseconds);
        }
    }

    public class Person
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public List<Email> Emails { get; set; }
    }

    public class Email
    {
        public Guid Id { get; set; }

        public Guid PersonId { get; set; }

        public string Address { get; set; }
    }

    public class PersonConfiguration : EntityConfiguration<Person>
    {
        protected override void Configure(EntityBuilder<Person> entityBuilder)
        {
            entityBuilder.MapGuidGenerator(x => x.Id)
                         .MapOneToMany(x => x.Emails, x => x.Id, x => x.PersonId);
        }
    }

    public class EmailConfiguration : EntityConfiguration<Email>
    {
        protected override void Configure(EntityBuilder<Email> entityBuilder)
        {
            entityBuilder.MapGuidGenerator(x => x.Id);
        }
    }
}
