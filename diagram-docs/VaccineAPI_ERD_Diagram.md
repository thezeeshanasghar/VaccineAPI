```mermaid
erDiagram
    USER {
        long Id PK
        string MobileNumber
        string Password
        string UserType
        string CountryCode
    }
    
    CHILD {
        long Id PK
        string Name
        string Guardian
        string FatherName
        string Email
        datetime DOB
        string Gender
        string City
        string Agent
        string CNIC
        bool IsEPIDone
        bool IsVerified
        bool IsInactive
        string Type
        long ClinicId FK
        long UserId FK
    }
    
    DOCTOR {
        long Id PK
        string Name
        string PMDC
        string Contact
        string OffDays
        bool IsActive
        int Fee
        string Degree
        int VisitDays
        string Image
        long ClinicId FK
        long UserId FK
    }
    
    CLINIC {
        long Id PK
        string Name
        string Address
        string Contact
        string Logo
        string Email
        string HeaderTitle
        int FollowUpCount
        int SMSNotificationDay
    }
    
    VACCINE {
        long Id PK
        string Name
        int MinAge
        int MaxAge
        bool isInfinite
    }
    
    BRAND {
        long Id PK
        string Name
        long VaccineId FK
    }
    
    DOSE {
        long Id PK
        string Title
        int MinAge
        int MaxAge
        bool IsActive
        int Duration
        bool IsSpecial
        long VaccineId FK
    }
    
    SCHEDULE {
        long Id PK
        string Status
        datetime AppointmentDate
        long ChildId FK
    }
    
    FOLLOW_UP {
        long Id PK
        datetime FollowUpDate
        string Reason
        string Type
        long ChildId FK
    }
    
    CLINIC_TIMING {
        long Id PK
        string Day
        string StartTime
        string EndTime
        long ClinicId FK
    }
    
    INVOICE {
        int Id PK
        string InvoiceId
        decimal Amount
        int ChildId FK
        int DoctorId FK
        int ClinicId FK
        int DoseId FK
    }
    
    DOCTOR_SCHEDULE {
        long Id PK
        string Day
        string StartTime
        string EndTime
        long DoctorId FK
    }
    
    STOCK {
        long Id PK
        int Quantity
        long BrandId FK
        long ClinicId FK
    }
    
    BRAND_AMOUNT {
        long Id PK
        int Amount
        int Count
        long DoctorId FK
        long BrandId FK
    }
    
    MESSAGE {
        long Id PK
        string Text
        datetime Date
        long UserId FK
    }
    
    USER ||--o{ CHILD : "has"
    USER ||--o{ DOCTOR : "has"
    USER ||--o{ MESSAGE : "has"
    
    CHILD }o--|| CLINIC : "belongs to"
    CHILD }o--|| USER : "belongs to"
    
    CHILD ||--o{ FOLLOW_UP : "has"
    CHILD ||--o{ SCHEDULE : "has"
    
    DOCTOR }o--|| CLINIC : "belongs to"
    DOCTOR }o--|| USER : "belongs to"
    DOCTOR ||--o{ DOCTOR_SCHEDULE : "has"
    
    CLINIC ||--o{ DOCTOR : "has"
    CLINIC ||--o{ CHILD : "has"
    CLINIC ||--o{ CLINIC_TIMING : "has"
    
    VACCINE ||--o{ BRAND : "has"
    VACCINE ||--o{ DOSE : "has"
    
    BRAND }o--|| VACCINE : "belongs to"
    BRAND ||--o{ STOCK : "has"
    BRAND ||--o{ BRAND_AMOUNT : "has"
    
    DOSE }o--|| VACCINE : "belongs to"
    
    STOCK }o--|| CLINIC : "belongs to"
    STOCK }o--|| BRAND : "belongs to"
    
    BRAND_AMOUNT }o--|| DOCTOR : "belongs to"
    BRAND_AMOUNT }o--|| BRAND : "belongs to"
    
    INVOICE }o--|| CHILD : "belongs to"
    INVOICE }o--|| DOCTOR : "belongs to"
    INVOICE }o--|| CLINIC : "belongs to"
    INVOICE }o--|| DOSE : "belongs to"