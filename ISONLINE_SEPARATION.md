# IsOnline Status Separation: Doctor vs Personal Assistant

## Overview
The `IsOnline` status has been separated between Doctors and Personal Assistants (PAs) to allow independent management of online clinic status.

## Changes Made

### 1. Database Model Changes
- **PaAccess Model**: Added `IsOnline` property (bool) to track PA-specific online status per clinic
- **Clinic Model**: `IsOnline` property remains for doctor-specific online status (unchanged)

### 2. Controller Updates

#### PaAccessController
- **Create**: Automatically sets `IsOnline = true` for the first clinic assigned to a PA
- **Update**: Now includes `IsOnline` in the update operation
- **New Endpoint**: `PUT /api/paaccess/{id}/isonline` - Allows updating IsOnline status for a specific PA's clinic access
  - Logic: When setting a clinic to online, all other clinics for that PA are set to offline (similar to doctor behavior)
  - If it's the only clinic for the PA, it's automatically set to online

#### PersonalAssistantController
- **GetClinicsByPaId**: Updated to return PA-specific `IsOnline` status from `PaAccess` instead of `Clinic.IsOnline`
  - Returns `IsOnline` from `PaAccess` table
  - Includes `PaAccessId` in the response for easy reference

### 3. DTO Updates
- **PaAccessDTO**: Added `IsOnline` property

## How It Works

### For Doctors:
- Doctors use `Clinic.IsOnline` to mark which clinic is currently online
- Only one clinic per doctor can be online at a time
- Managed through `ClinicController.Edit` endpoint

### For Personal Assistants:
- PAs use `PaAccess.IsOnline` to mark which clinic is currently online for them
- Each PA has independent control over their online status per clinic
- Only one clinic per PA can be online at a time
- Managed through `PaAccessController` endpoints

## API Endpoints

### Update PA's Online Status
```
PUT /api/paaccess/{paAccessId}/isonline
Body: true/false
```

### Get Clinics for PA (with PA-specific IsOnline)
```
GET /api/personalassistant/clinics/{paId}
Returns: List of clinics with IsOnline from PaAccess table
```

## Important Notes

1. **Existing Queries**: Queries in `ChildController` and `ScheduleController` that check `x.Clinic.IsOnline == true` are doctor-specific and correctly use `Clinic.IsOnline`. These do not need changes unless PA-specific versions are required.

2. **Migration Required**: A database migration is needed to add the `IsOnline` column to the `PaAccess` table. Default value should be `false` for existing records.

3. **Backward Compatibility**: The `Clinic.IsOnline` property remains unchanged, ensuring backward compatibility with existing doctor functionality.

## Example Usage

### Setting a clinic as online for a PA:
```csharp
PUT /api/paaccess/123/isonline
Body: true
```

### Getting clinics for a PA (with PA-specific online status):
```csharp
GET /api/personalassistant/clinics/456
Response includes: IsOnline from PaAccess table
```
