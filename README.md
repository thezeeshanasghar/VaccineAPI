dotnet clean
dotnet restore
dotnet build
dotnet run

dotnet ef migrations add InitialCreate
dotnet ef database update

