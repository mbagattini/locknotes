# Lock Notes

- Applicazione Windows desktop per la gestione di file di testo cifrati (`.stxt`).  
- Il testo in chiaro non viene mai scritto su disco.

## Build e publish

- Usa dotnet build per compilare, non msbuild
- Comando: dotnet build LockNotes\LockNotes.csproj -p:Platform=x64
- Il flag -p:Platform=x64 e' obbligatorio: senza, la build AnyCPU fallisce con "WindowsAppSDKSelfContained requires a supported Windows architecture"

## Boundaries

- Tool a uso interno, non applicare criteri per applicazioni da produzione
- Niente unit test
- Niente overcomplicazioni
- Il progetto deve poter girare dentro Visual Studio 2022
- Compila al termine delle modifiche e assicurati che la build abbia successo
- Chiedi se pubblicare al pilota dopo un set di modifiche significativo