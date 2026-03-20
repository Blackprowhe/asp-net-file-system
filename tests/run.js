import { group, runTests } from "./framework.js";
import { isAdvanced as isGroup, printExampleCommands, startProgram } from "./inputHandler.js";
import { registerApiFilesPost } from "./tests/v1-api-files-post.js";
import { registerApiLogin } from "./tests/v1-api-login.js";
import { registerIndexHtmlTests } from "./tests/v1-index-html.js";
import { registerSignalR } from "./tests/v2-signalr.js";

await startProgram(printHelp);

group("Vecka 1", () => {
    registerIndexHtmlTests();
    registerApiFilesPost();

    if (isGroup()) {
        registerApiLogin();
    }
});

group("Vecka 2", () => {
    registerSignalR();
});



const result = await runTests();
process.exit(result ? 0 : 1);



function printHelp() {
    console.log("Testerna används för att säkerställa att programmet fungerar som tänkt. För att")
    console.log("bli godkänd måste alla tester vara gröna, men kom ihåg att det tillkommer tester")
    console.log("för de som jobbar som grupp. Se nedan.")
    console.log()
    printExampleCommands("npm test");
}