```mermaid
classDiagram
    User "1" --> "*" Child : has
    User "1" --> "*" Doctor : has
    User "1" --> "*" Message : has
    
    Child "1" --> "*" FollowUp : has
    Child "1" --> "*" Schedule : has
    Child "*" --> "1" Clinic : belongs to
    Child "*" --> "1" User : belongs to
    
    Doctor "1" --> "*" DoctorSchedule : has
    Doctor "*" --> "1" Clinic : belongs to
    Doctor "*" --> "1" User : belongs to
    
    Clinic "1" --> "*" Doctor : has
    Clinic "1" --> "*" Child : has
    Clinic "1" --> "*" ClinicTiming : has
    
    Vaccine "1" --> "*" Brand : has
    Vaccine "1" --> "*" Dose : has
    
    Brand "*" --> "1" Vaccine : belongs to
    Brand "1" --> "*" Stock : has
    Brand "1" --> "*" BrandAmount : has
    
    Dose "*" --> "1" Vaccine : belongs to
    
    Stock "*" --> "1" Clinic : belongs to
    Stock "*" --> "1" Brand : belongs to
    
    BrandAmount "*" --> "1" Doctor : belongs to
    BrandAmount "*" --> "1" Brand : belongs to
    
    Invoice "*" --> "1" Child : belongs to
    Invoice "*" --> "1" Doctor : belongs to
    Invoice "*" --> "1" Clinic : belongs to
    Invoice "*" --> "1" Dose : belongs to
    
    class User {
        +long Id
        +string MobileNumber
        +string Password
        +string UserType
        +string CountryCode
    }
    
    class Child {
        +long Id
        +string Name
        +string Guardian
        +string FatherName
        +string Email
        +DateTime DOB
        +string Gender
        +string City
        +string Agent
        +string CNIC
        +bool? IsEPIDone
        +bool? IsVerified
        +bool? IsInactive
        +string Type
        +long ClinicId
        +long UserId
    }
    
    class Doctor {
        +long Id
        +string Name
        +string PMDC
        +string Contact
        +string OffDays
        +bool IsActive
        +int Fee
        +string Degree
        +int VisitDays
        +string Image
        +long ClinicId
        +long UserId
    }
    
    class Clinic {
        +long Id
        +string Name
        +string Address
        +string Contact
        +string Logo
        +string Email
        +string HeaderTitle
        +int FollowUpCount
        +int SMSNotificationDay
    }
    
    class Vaccine {
        +long Id
        +string Name
        +int MinAge
        +int? MaxAge
        +bool isInfinite
    }
    
    class Brand {
        +long Id
        +string Name
        +long VaccineId
    }
    
    class Dose {
        +long Id
        +string Title
        +int MinAge
        +int? MaxAge
        +bool IsActive
        +int Duration
        +bool IsSpecial
        +long VaccineId
    }
    
    class Schedule {
        +long Id
        +string Status
        +DateTime AppointmentDate
        +long ChildId
    }
    
    class FollowUp {
        +long Id
        +DateTime FollowUpDate
        +string Reason
        +string Type
        +long ChildId
    }
    
    class ClinicTiming {
        +long Id
        +string Day
        +string StartTime
        +string EndTime
        +long ClinicId
    }
    
    class Invoice {
        +int Id
        +string InvoiceId
        +decimal Amount
        +int ChildId
        +int DoctorId
        +int ClinicId
        +int DoseId
    }
    
    class DoctorSchedule {
        +long Id
        +string Day
        +string StartTime
        +string EndTime
        +long DoctorId
    }
    
    class Stock {
        +long Id
        +int Quantity
        +long BrandId
        +long ClinicId
    }
    
    class BrandAmount {
        +long Id
        +int Amount
        +int Count
        +long DoctorId
        +long BrandId
    }
    
    class Response~T~ {
        +T ResponseData
        +bool IsSuccess
        +string Message
    }