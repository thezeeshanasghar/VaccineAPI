dotnet clean
dotnet restore
dotnet build
dotnet run

dotnet tool install --global dotnet-ef --version 3.\*
dotnet ef migrations add InitialCreate
dotnet ef database update

dotnet tool install --global dotnet-ef

select \* from childs where city is null;

update childs set city='' where city is null
dotnet watch run --launch-profile https

"ConnectionStrings": {
"DefaultConnection": "server=localhost;userid=root;password=;database=fernfers_vaccinebe;port=3306;"
},

"DefaultConnection": "server=localhost;userid=skint\_\_stagedb;password=mcs@12345;database=skinthno_stagedb;port=3306;"
