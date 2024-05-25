dotnet clean
dotnet restore
dotnet build
export ASPNETCORE_ENVIRONMENT=Development
dotnet run

dotnet tool install --global dotnet-ef --version 3.\*
dotnet ef migrations add InitialCreate
dotnet ef database update

dotnet tool install --global dotnet-ef

select * from childs where city is null;
update Doctors set city = "" where city is null;
select * from Doctors where ProfileImage is null;
update Doctors set ProfileImage = "" where ProfileImage is null;

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

ALTER TABLE childs
DROP COLUMN PreferredSchedule;

ALTER TABLE childs
DROP COLUMN PreferredDayOfReminder;

ALTER TABLE childs
DROP COLUMN PreferredDayOfWeek;

ALTER TABLE doses
DROP COLUMN IsSpecial;

ALTER TABLE childs
DROP COLUMN Type;





CREATE TABLE `cities` (
  `Id` INT NOT NULL AUTO_INCREMENT,
  `Name` VARCHAR(255) NOT NULL,
  PRIMARY KEY (`Id`)
);

 INSERT INTO `cities`(`Name`) VALUES ('Abbottabad'), ('Adezai'), ('Ali Bandar'), ('Amir Chah'), ('Attock'), ('Ayubia'), ('Bahawalpur'), ('Baden'), ('Bagh'), ('Bahawalnagar'), ('Burewala'), ('Banda Daud Shah'), ('Bannu'), ('Batagram'), ('Bazdar'), ('Bela'), ('Bellpat'), ('Bhag'), ('Bhakkar'), ('Bhalwal'), ('Bhimber'), ('Birote'), ('Buner'), ('Burj'), ('Chiniot'), ('Chachro'), ('Chagai'), ('Chah Sandan'), ('Chailianwala'), ('Chakdara'), ('Chakku'), ('Chakwal'), ('Chaman'), ('Charsadda'), ('Chhatr'), ('Chichawatni'), ('Chitral'), ('Dadu'), ('Dera Ghazi Khan'), ('Dera Ismail Khan'), ('Dalbandin'), ('Dargai'), ('Darya Khan'), ('Daska'), ('Dera Bugti'), ('Dhana Sar'), ('Digri'), ('Dina'), ('Dinga'), ('Diplo'), ('Diwana'), ('Dokri'), ('Drosh'), ('Duki'), ('Dushi'), ('Duzab'), ('Faisalabad'), ('Fateh Jang'), ('Ghotki'), ('Gwadar'), ('Gujranwala'), ('Gujrat'), ('Gadra'), ('Gajar'), ('Gandava'), ('Garhi Khairo'), ('Garruck'), ('Ghakhar Mandi'), ('Ghanian'), ('Ghauspur'), ('Ghazluna'), ('Girdan'), ('Gulistan'), ('Gwash'), ('Hyderabad'), ('Hala'), ('Haripur'), ('Hab Chauki'), ('Hafizabad'), ('Hameedabad'), ('Hangu'), ('Harnai'), ('Hasilpur'), ('Haveli Lakha'), ('Hinglaj'), ('Hoshab'), ('Islamabad'), ('Islamkot'), ('Ispikan'), ('Jacobabad'), ('Jamshoro'), ('Jhang'), ('Jhelum'), ('Jamesabad'), ('Jampur'), ('Janghar'), ('Jati (Mughalbhin)'), ('Jauharabad'), ('Jhal'), ('Jhal Jhao'), ('Jhatpat'), ('Jhudo'), ('Jiwani'), ('Jungshahi'), ('Karachi'), ('Kotri'), ('Kalam'), ('Kalandi'), ('Kalat'), ('Kamalia'), ('Kamararod'), ('Kamber'), ('Kamokey'), ('Kanak'), ('Kandi'), ('Kandiaro'), ('Kanpur'), ('Kapip'), ('Kappar'), ('Karak City'), ('Karodi'), ('Kashmor'), ('Kasur'), ('Katuri'), ('Keti Bandar'), ('Khairpur'), ('Khanaspur'), ('Khanewal'), ('Kharan'), ('Kharian'), ('Khokhropur'), ('Khora'), ('Khushab'), ('Khuzdar'), ('Kikki'), ('Klupro'), ('Kohan'), ('Kohat'), ('Kohistan'), ('Kohlu'), ('Korak'), ('Korangi'), ('Kot Sarae'), ('Kotli'), ('Lahore'), ('Larkana'), ('Lahri'), ('Lakki Marwat'), ('Lasbela'), ('Latamber'), ('Layyah'), ('Leiah'), ('Liari'), ('Lodhran'), ('Loralai'), ('Lower Dir'), ('Shadan Lund'), ('Multan'), ('Mandi Bahauddin'), ('Mansehra'), ('Mian Chanu'), ('Mirpur'), ('Moro'), ('Mardan'), ('Mach'), ('Madyan'), ('Malakand'), ('Mand'), ('Manguchar'), ('Mashki Chah'), ('Maslti'), ('Mastuj'), ('Mastung'), ('Mathi'), ('Matiari'), ('Mehar'), ('Mekhtar'), ('Merui'), ('Mianwali'), ('Mianez'), ('Mirpur Batoro'), ('Mirpur Khas'), ('Mirpur Sakro'), ('Mithi'), ('Mongora'), ('Murgha Kibzai'), ('Muridke'), ('Musa Khel Bazar'), ('Muzaffar Garh'), ('Muzaffarabad'), ('Nawabshah'), ('Nazimabad'), ('Nowshera'), ('Nagar Parkar'), ('Nagha Kalat'), ('Nal'), ('Naokot'), ('Nasirabad'), ('Nauroz Kalat'), ('Naushara'), ('Nur Gamma'), ('Nushki'), ('Nuttal'), ('Okara'), ('Ormara'), ('Peshawar'), ('Panjgur'), ('Pasni City'), ('Paharpur'), ('Palantuk'), ('Pendoo'), ('Piharak'), ('Pirmahal'), ('Pishin'), ('Plandri'), ('Pokran'), ('Pounch'), ('Quetta'), ('Qambar'), ('Qamruddin Karez'), ('Qazi Ahmad'), ('Qila Abdullah'), ('Qila Ladgasht'), ('Qila Safed'), ('Qila Saifullah'), ('Rawalpindi'), ('Rabwah'), ('Rahim Yar Khan'), ('Rajan Pur'), ('Rakhni'), ('Ranipur'), ('Ratodero'), ('Rawalakot'), ('Renala Khurd'), ('Robat Thana'), ('Rodkhan'), ('Rohri'), ('Sialkot'), ('Sadiqabad'), ('Safdar Abad- (Dhaban Singh)'), ('Sahiwal'), ('Saidu Sharif'), ('Saindak'), ('Sakrand'), ('Sanjawi'), ('Sargodha'), ('Saruna'), ('Shabaz Kalat'), ('Shadadkhot'), ('Shahbandar'), ('Shahpur'), ('Shahpur Chakar'), ('Shakargarh'), ('Shangla'), ('Sharam Jogizai'), ('Sheikhupura'), ('Shikarpur'), ('Shingar'), ('Shorap'), ('Sibi'), ('Sohawa'), ('Sonmiani'), ('Sooianwala'), ('Spezand'), ('Spintangi'), ('Sui'), ('Sujawal'), ('Sukkur'), ('Suntsar'), ('Surab'), ('Swabi'), ('Swat'), ('Tando Adam'), ('Tando Bago'), ('Tangi'), ('Tank City'), ('Tar Ahamd Rind'), ('Thalo'), ('Thatta'), ('Toba Tek Singh'), ('Tordher'), ('Tujal'), ('Tump'), ('Turbat'), ('Umarao'), ('Umarkot'), ('Upper Dir'), ('Uthal'), ('Vehari'), ('Veirwaro'), ('Vitakri'), ('Wadh'), ('Wah Cantt'), ('Warah'), ('Washap'), ('Wasjuk'), ('Wazirabad'), ('Yakmach'), ('Zhob'), ('Other'); 
