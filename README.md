# ad-sync-to-cbr


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
- `cbr-server`: Collaborator cusromers url in format `https://domain.com`
- `cbr-login`: Collaborator authorization login
- `cbr-password`: Collaborator authorization password


Available run flags:
- `--save-local`: Will overwrite `ad-save-local` from config
- `--append-mode`: Will overwrite `ad-append-mode` from config
- `--debug-ad`: Output selected from LDAP users with attributes to stdout
