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
dotnet watch run --environment Development
Step 1: 
ALTER TABLE clinics
DROP COLUMN OffDays;

ALTER TABLE doctors
DROP COLUMN SignatureImage;

Step 2:
dotnet ef migrations add InitialCreate
dotnet ef database update