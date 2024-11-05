# ad-sync-to-cbr

This is Open-Source utilite for sync users from Microsoft Active Directory and LMS Collaborator. This project is not part of LMS Collaborator see details in License file.

## Config parameters

Available config parameters:

- `ad-server`: LDAP server config in format `LDAP://10.0.0.1/ou=орг,dc=tmp,dc=corp` where `10.0.0.1` LDAP server IP adress, `/ou=орг,dc=tmp,dc=corp` optional LDAP search query. You can define multiple LDAP connections splited by `;`.
- `ad-username`: LDAP authorization login
- `ad-password`: LDAP authorization password
- `ad-filter`: LDAP filter, see https://docs.microsoft.com/dotnet/api/system.directoryservices.directorysearcher.filter?view=netframework-4.8#System_DirectoryServices_DirectorySearcher_Filter
- `ad-email-sufix`: Optional email sufix for users without email in format `@domain.com
- `ad-extract-tags`: Optional parameters for extract user LDAP attributes as tags in format `distinguishedname=OU;memberof=CN,OU`
- `ad-sync-photos`: Flag for sync photos
- `ad-save-local`: Otional flag, if set `true` will save extracted from LDAP user to local file `rusers.csv`
- `ad-append-mode`: Otional flag, if set `true` will append data to file `rusers.csv`
- `cbr-server`: LMS Collaborator cusromers url in format `https://domain.com`
- `cbr-token`: LMS Collaborator API token
- `cbr-field-map:XXX`: LMS Collaborator user field map, see datails in 'Mapping'

## CLI run flags

Available run flags:
- `--save-local`: Will overwrite `ad-save-local` from config
- `--append-mode`: Will overwrite `ad-append-mode` from config
- `--debug-ad`: Output selected from LDAP users with attributes to stdout

## User fields mappgin

You can map LMS Collaborator user profile fields to AD fields by add params in to config:
Key: `cbr-field-map:XXX`, where `XXX` is LMS user field name. 
Value: AD field name.
Example: `<add key="cbr-field-map:patronymic" value="initials"/>` will map `patronymic` field in LMS Collaborator to `initials` (Middlename in AD UI).
LMS Collaborator user avaiable fields:
- secondname
- firstname
- patronymic
- login
- email
- birth_date
- gender
- city
- department
- position
- tags
- phone
- date_of_employment
- work_contact
- date_of_assignment_current_position
- structure_uid
- user_field1
- user_field2
- user_field3
- user_field4
- user_field5
- customInfo
- language
