# PostgreSQL.Bulk

<a href="https://www.nuget.org/packages/PostgreSQL.Bulk"><img alt="Nuget" src="https://img.shields.io/nuget/v/PostgreSQL.Bulk"></a> <a href="https://www.nuget.org/packages/PostgreSQL.Bulk"><img alt="Nuget" src="https://img.shields.io/nuget/dt/PostgreSQL.Bulk"></a> <a href="https://github.com/TwentyFourMinutes/PostgreSQL.Bulk/issues"><img alt="GitHub issues" src="https://img.shields.io/github/issues-raw/TwentyFourMinutes/PostgreSQL.Bulk"></a> <a href="https://github.com/TwentyFourMinutes/PostgreSQL.Bulk/blob/master/LICENSE"><img alt="GitHub" src="https://img.shields.io/github/license/TwentyFourMinutes/PostgreSQL.Bulk"></a> <a href="https://discordapp.com/invite/EYKxkce"><img alt="Discord" src="https://discordapp.com/api/guilds/275377268728135680/widget.png"></a>

PostgreSQL.Bulk is designed to provide a fast wrapper around the Npgsql's `BinaryImporter` class which allows the insertion of Data in a bulk  fashion. It will allows you to easily insert millions of rows at once with or without foreign tables.

## About

This packages aims to provide near native speed of calling the `BinaryImporter` methods, while providing automatic mapping of columns and much more. This removes the need of writing long boiler code for every Entity (Table).

## Installation

You can either get this package by downloading it from the NuGet Package Manager built in Visual Studio or from the official [nuget.org](https://www.nuget.org/packages/PostgreSQL.Bulk) website. 

Also you can install it via the **P**ackage **M**anager **C**onsole:

```
PM> Install-Package PostgreSQL.Bulk
```

## Example

For the first Step, you will need to create a Model which represents a given table inside your PostgreSQL Database. For the sake of this example, we will create two models, one for our Person and one for its E-Mail addresses.

```c#
public class Person
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public List<Email> Emails { get; set; }
}

public class Email
{
    public Guid Id { get; set; }

    public string Address { get; set; }
    
    public Guid PersonId { get; set; }
}
```

As you can see those two entities belong together, hence the `List<Email>`. In order to tell the package that, you will need to add an `EntityConfiguration` for the Person. 

The `MapGuidGenerator` will tell the package to generate a new `Guid` for the primary key, if none was defined. This could also be achieved by using the `MapValueFactory` method. Whereas `MapOneToMany`, will tell the package that if your Person has Emails, they should be inserted as well. This also specifies that the `Person.Id` should get copied over to the `PersonId`. 

```c#
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
```

Now at the start of our application we would want to call `EntityConfigurator.BuildConfigurations()`, in order to build the configuration we just defined.  For the last step we just need to get some Persons ready and open up a Npgsql connection.

```c#
await using (var connection = new NpgsqlConnection("YourConnectionString"))
{
    await connection.OpenAsync();

    var persons = new List<Person>();

    for (int i = 0; i < 1000000; i++)
    {
        list.Add(new Person()
                 {
                     Name = "Person Nr." + i.ToString(),
                     Emails = new List<Email> { new Email { Address = "person" + i.ToString() + "@example.com" } }
                 });
    }

    await connection.BulkInsertAsync(persons);
}
```

And that's already it, all items should successfully be inserted to your database, with the appropriately foreign key set.

## How it works

Under the hood this package relies heavily on compiled expressions and its cache, which allow for near native performance. 
The Cache for a Model will be build under two specific circumstances:

1. You as the user call `EntityConfigurator.BuildConfigurations` on your own, with all the assemblies which contain the `EntityConfiguration` classes. This should preferably be called at the start of your application. _(recommended way)_
2. If for some reason the `EntityConfigurator.BuildConfigurations` method wasn't called, it will perform the build of all the configurations found in the current assembly, once you call `BulkInsertAsync` for the first time. Note that this will obviously slow down the first call of `BulkInsertAsync`, by a lot. _(not recommended way)_

#### Internal Behaviours

- Table names - are automatically generated by the name of the Entity with an 's' appended, if not appropriate call `MapToTable` 
- Column names - are automatically generated by the name of the Property inside the entity, if not appropriate call `MapToColumn` 
- The following attributes which are defined in `System.ComponentModel.DataAnnotations.Schema` are supported:
  - `NotMapped`
  - `Table`
  - `Column`
- Your Configurations should always represent the tree from top to bottom, that's the reason to why there is no `MapManyToOne` method.

### Contact information

If you feel like something is not working as intended or you are experiencing issues, feel free to create an issue. Also for feature requests just create an issue. For further information feel free to send me a [mail](mailto:office@twenty-four.dev) to `office@twenty-four.dev` or message me on Discord `24_minutes#7496`.