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

## TODO

In questo paragrafo raccolgo funzionalità da implementare gradualmente, per riferimento del pilota e dell'assistente.

- Supporto per ambiente multi-utente: in corso di sviluppo
- Supporto per il recupero di file tramite password di emergenza: in corso di sviluppo
- Introduzione di test: la direttiva attuale è "no unit test", ma l'applicazione sta crescendo più del previsto. L'assistente ha suggerito di introdurre test limitati, evitando di testare il layer di interfaccia. Andrà esternalizzata in una libreria separata la logica di criptazione e tutto ciò che non fa parte della UI, quindi implementati i test.
