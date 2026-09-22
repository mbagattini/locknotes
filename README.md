# Lock Notes

Applicazione desktop per Windows (WinUI 3, .NET 9) per creare e gestire file di testo cifrati con estensione `.stxt`. Il testo in chiaro non viene mai scritto su disco: il contenuto vive solo in memoria e viene cifrato a ogni salvataggio.

## Funzionalita'

- Editor a tab in stile Notepad, con la tab strip integrata nella title bar
- Cifratura AES-256-CBC con autenticazione HMAC-SHA256 (Encrypt-then-MAC)
- Derivazione della chiave con PBKDF2-SHA256 (100.000 iterazioni, salt casuale per blocco)
- Codice di recupero per ogni file: 24 caratteri Base32 in gruppi da 4 (es. `XXXX-XXXX-XXXX-XXXX-XXXX-XXXX`), utilizzabile per lo sblocco d'emergenza se la password viene dimenticata
- Cambio password e visualizzazione del codice di recupero dal menu
- Supporto multi-utente su share di rete: rilevamento delle modifiche esterne tramite polling (il FileSystemWatcher e' inaffidabile su SMB/VPN) e ricaricamento del file salvato da altri utenti
- Istanza singola: l'apertura di un nuovo file viene reindirizzata all'istanza gia' attiva
- Ripristino della sessione: i tab aperti vengono riaperti (bloccati) al riavvio
- Icona nella system tray
- Incolla come testo semplice, zoom del font con Ctrl+/-, scorciatoie standard (Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+W)
- Tema scuro con sfondo Mica (dove supportato)

## Formato file .stxt

- Header: magic `LKN2` (4 byte) seguito dal byte di versione
- Blocco recovery: codice di recupero cifrato con la password (mostrato a chi conosce la password)
- Blocco password: password cifrata con il codice di recupero (per lo sblocco d'emergenza)
- Body: contenuto del documento cifrato con la password
- Ogni blocco e' un container autonomo: `salt(16) | iv(16) | ciphertext | hmac(32)` con HMAC calcolato su salt, iv e ciphertext

## Requisiti

- Windows 10 versione 1809 (build 17763) o successiva
- .NET 9 SDK per la compilazione
- Visual Studio 2022 (opzionale, il progetto e' apribile e compilabile dall'IDE)

## Build

- Compilazione: `dotnet build LockNotes\LockNotes.csproj -p:Platform=x64`
- Il flag `-p:Platform=x64` e' obbligatorio: senza, la build AnyCPU fallisce per via di `WindowsAppSdkSelfContained`

## Publish

- Comando: `dotnet publish LockNotes\LockNotes.csproj -c Release -r win-x64 -o <cartella>`
- In alternativa lo script `publish.bat` (modificare il percorso di destinazione al suo interno)
- Il risultato e' un eseguibile singolo self-contained: non richiede .NET ne' Windows App SDK installati sulla macchina di destinazione

## Note

- Tool a uso interno: non sono applicati i criteri tipici delle applicazioni di produzione
- La perdita contemporanea di password e codice di recupero rende il file irrecuperabile per progettazione
