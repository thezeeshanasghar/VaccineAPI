dotnet clean
dotnet restore
dotnet build
dotnet run

dotnet tool install --global dotnet-ef --version 3.*
dotnet ef migrations add InitialCreate
dotnet ef database update

dotnet tool install --global dotnet-ef

