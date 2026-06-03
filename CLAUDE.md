# Lock Notes

Applicazione Windows desktop per la gestione di file di testo cifrati (`.stxt`).  
Il testo in chiaro non viene mai scritto su disco.

## Build

- Usa dotnet build per compilare, non msbuild

## Boundaries

- Tool a uso interno, non applicare criteri per applicazioni da produzione
- Niente unit test
- Niente overcomplicazioni
- Il progetto deve poter girare dentro Visual Studio 2022
- Compila al termine delle modifiche e assicurati che la build abbia successo