let currentFile = null;
let isViewingHistory = false;
let isSaving = false;

// skapar SignalR anslutning
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/api/events/signalr")
    .build();

// hämtar html element som används i scriptet
const fileList = document.getElementById("fileList");
const fileName = document.getElementById("fileName");
const editor = document.getElementById("editor");
const saveButton = document.getElementById("saveButton");
const deleteButton = document.getElementById("deleteButton");
const uploadButton = document.getElementById("uploadButton");
const uploadFileInput = document.getElementById("uploadFileInput");

// Lyssnar på realtidshändelser från servern via SignalR.
// När en fil ändras, skapas eller tas bort skickas ett event med typ och sökväg.
//
// Koden filtrerar bort irrelevanta händelser (t.ex. från andra mappar)
// och ignorerar uppdateringar under vissa tillstånd (t.ex. vid sparning eller historikvy).
//
// Därefter uppdateras fillistan dynamiskt.
// Om den aktuella filen har tagits bort rensas UI:t (filnamn och editor).


connection.on("Event", async function (type, path) {



    // loggar så man kan se när koden uprrepat pajjar.
    console.log("SignalR event:", type, path);

   // tar bort eventuella trailing slashar för att jämföra paths korrekt
   const normalizedPath = path.replace(/\/$/, "");

   // filtrerar bort irrelevanta events som inte påverkar
   //  den aktuella vyn
if (currentPath && !normalizedPath.startsWith(currentPath.replace(/\/$/, ""))) {
    return;
}
    // hoppar över uppdateringar om man tittar på historiken
    //  sparar för att undvika påverkande uppdateringar
     if (isViewingHistory) return;

     if (isSaving) return;

    
    // uppdatera listan
    await loadFiles(currentPath);

    // hanterar borttagen fil
    if (currentFile === path) {
        if (type === 2) {
            // fil borttagen

            // förutsatt att detta stämmer.
            currentFile = null;
            fileName.textContent = "Ingen fil vald";
            editor.value = "";
       
            
        }
    }
});

// Laddar filer och mappar från backend baserat på aktuell sökväg.
// Uppdaterar currentPath och visar den i UI:t.
//
// Hämtar data från API (/api/files/{path}) och hanterar olika svarformat,
// där innehållet antingen kan

let currentPath = "";

// funktion för att ladda filer
async function loadFiles(path = "") {
    currentPath = path;

// uppdaterar den visade sökvägen i UI:t
    document.getElementById("currentPathDisplay").textContent =
    currentPath ? "/" + currentPath : "/";

    // anropar API:t för att hämta filer i den aktuella sökvägen
    const response = await fetch(`/api/files/${path}`);
const result = await response.json();

//  om conten finns använd det annars använd hela objektet

let data = {};


// kollar att resultatet är ett objekt och kollar att det inte är null
// Säkerställer att vi får rätt dataformat från API:t.
// Hanterar både fall där svaret är direkt data eller ligger inuti "content".
//
// Detta gör koden mer stark mot olika svar från backend.
// Loggar även information för felsökning och rensar fillistan innan ny data renderas.
if (result && typeof result === "object") {
    if (result.content && typeof result.content === "object") {
        data = result.content;
    } else {
        data = result;
    }
}

//  loggar för att felsöka

console.log("PATH:", path);
console.log("RESULT:", result);
console.log("DATA:", data);

// tömmer listan innan vi lägger till nya element

fileList.innerHTML = "";

// visa tom mapp
if (Object.keys(data).length === 0) {
    const empty = document.createElement("div");
    empty.textContent = "(Tom mapp)";
    empty.style.opacity = "0.6";
    empty.style.padding = "10px";
    fileList.appendChild(empty);
    return; // viktigt!
}

// annars loopa filer/mappar
for (const name in data) {
    const item = document.createElement("div");
    item.className = "file-item";

    const fileData = data[name];

    if (!fileData.file) {
        item.textContent = "📁 " + name;
    } else {
        item.textContent = "📄 " + name;
    }

    item.addEventListener("click", async () => {
        document.querySelectorAll(".file-item")
            .forEach(x => x.classList.remove("active"));

        item.classList.add("active");

        if (!fileData.file) {
            await loadFiles(currentPath + name + "/");
            return;
        }

        await openFile(currentPath + name);
    });

    fileList.appendChild(item);
}
}

// Laddar innehållet i en fil från backend och visar det i editorn.
//avgör om det är en bild eller text.

async function openFile(path) {
    console.log("openFile körs", path);

    /// sparar filen som är öppen och uppdaterar UI:t med filnamnet.
    currentFile = path;
    fileName.textContent = path;

    // anropar backend

    const response = await fetch(`/api/files/${path}`);

    if (!response.ok) {
        alert("Kunde inte läsa filen.");
        return;
    }

    // stänger historik läget
    // viktig för att inte blanda historik med faktisk data


    isViewingHistory = false;

    // kod för att bilder sak fungera i programmet
    if (path.match(/\.(png|jpg|jpeg|gif)$/i)) {
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);

    editor.style.display = "none";

    let img = document.getElementById("imagePreview");

    if (!img) {
        img = document.createElement("img");
        img.id = "imagePreview";
        img.style.maxWidth = "100%";
        document.querySelector(".main").appendChild(img);
    }

    img.src = url;
    img.style.display = "block";
    return;
}

    // annars text
    const text = await response.text();

    const img = document.getElementById("imagePreview");
    if (img) img.style.display = "none";

    editor.style.display = "block";
    editor.value = text;
    console.log("laddar history för", path);
    loadHistory(path);
}

// Sparar innehållet i editorn till backend.
// Sparar den aktuella filen till servern.
// Säkerställer att en fil är vald och hanterar specialfall som historikläge,
// där användaren kan skapa en ny version.
//
// Skickar en PUT-request med filens innehåll (text/plain) till backend.
// Under sparning sätts en flagga (isSaving) för att undvika konflikter
// med andra uppdateringar (t.ex. SignalR-events).
//
// Efter lyckad sparning laddas filen om och UI uppdateras.
async function saveFile() {

    // alertar användaren om ingen fil är vald
    if (!currentFile) {
        alert("Välj en fil först.");
        return;
    }

    // varnar användaren om de redigerar en historikversion
    if (isViewingHistory) {
        const confirmed = confirm("Du redigerar en gammal version. Vill du skapa en ny version?");
        if (!confirmed) return;
    }
    // sätter flaggor för att undvika att onödiga uppdateringar triggas under sparning
      isViewingHistory = false;
      isSaving = true;

    // skickar filens innehåll till servern.
    const response = await fetch(`/api/files/${currentFile}`, {
        // använder PUT för att uppdatera en befintlig fil
        method: "PUT",
        headers: {
            "Content-Type": "text/plain"
        },
        // skickar textinnehållet i editorn som body
        body: editor.value
    });
    // alert om något gick fel vid sparning.
    if (!response.ok) {
        alert("Kunde inte spara filen.");
            isSaving = false;
        return;
    }
    // liten delay för att backend sak hinna med att processa
    //  ändringen innan vi försöker ladda om filen.
     await new Promise(r => setTimeout(r, 100));


    // hämtar senaste versionen från server.
     await openFile(currentFile);

     // alert om att filen sparades
    alert("Filen sparades.");
    await loadFiles(currentPath);
        isSaving = true;
}

// // Raderar den aktuella filen från servern.
// Kontrollerar först att en fil är vald och ber användaren
//  bekräfta borttagningen.
// Skickar en DELETE-request till backend.
// Vid lyckad radering uppdateras UI:t genom att rensa den valda filen,
// tömmer editorn och ladda om fillistan.

async function deleteFile() {
    if (!currentFile) {
        alert("Välj en fil först.");
        return;
    }

    // varnar användaren innan radering

    const confirmed = confirm(`Är du säker på att du vill radera filen "${currentFile}"?`);
    if (!confirmed) {
        return;
    }

    // anropar backend för att radera filen
    // standard http metod för borttagning av filer

    const response = await fetch(`/api/files/${currentFile}`, {
        method: "DELETE"
    });

    if (!response.ok) {
        alert("Kunde inte radera filen.");
        return;
    }

    //uppdaterar UI:t efter radering

    alert("Filen raderades.");
    currentFile = null;
    fileName.textContent = "Ingen fil vald";
    editor.value = "";
    document.getElementById("history").innerHTML = "";
    await loadFiles(currentPath);
}

async function uploadFile() {
    const file = uploadFileInput.files[0];
    if (!file) {
        alert("Välj en fil att ladda upp.");
        return;
    }

    // läser innehållet i den valda filen som text

    const content = await file.text();

    // skickar post request till backend för att ladda upp filen

    const response = await fetch(`/api/files/${file.name}`, {
        method: "POST",
        headers: {
            "Content-Type": "text/plain"
        },
        body: content
    });

    if (response.status === 409) {
        alert("En fil med det namnet finns redan.");
        return;
    }

    if (!response.ok) {
        alert("Kunde inte ladda upp filen.");
        return;
    }

    uploadFileInput.value = "";
    await loadFiles();
    await openFile(file.name);

    alert("Filen laddades upp.");
}
// Hämtar och visar versionshistorik för en fil.
// Skickar en request till backend och får en lista med
//  tidigare versioner.
// Laddar historiken för en fil från backend och visar den i UI:t.
async function loadHistory(path) {
    const res = await fetch(`/api/files/history/${path}`);
    const history = await res.json();

    // hittar där historiken ska vara och rensar gammalt innehåll

    const container = document.getElementById("history");
    container.innerHTML ="";

    // loopar igenom alla versioner

    history.forEach(version => {
        const btn = document.createElement("button");
         const date = new Date(version.createdAt);
         const formatted = date.toLocaleString("sv-SE");

         btn.innerText = `Version ${version.version} (${formatted})`;

         //när man klickar laddas innehållet i editorn
         // och sätter isViewingHistory till true
        btn.onclick = () => {
    editor.value = version.content;
    isViewingHistory = true;
};

        // knappen visas på sidan för varje version
         container.appendChild(btn);
    });
}
 // hämtar text från input och trim tar bort onödiga mellanslag
 // Skapar en ny tom fil baserat på användarens input.
// Hämtar filnamnet från inputfältet och skickar en POST-request till backend.

async function createFile() {
    const input = document.getElementById("newFileName");
    const name = input.value.trim();

    if (!name) {
        alert("Ange ett filnamn.");
        return;
    }

    // skickar en POST-request till backend för att skapa en ny fil
    
    const response = await fetch(`/api/files/${currentPath + name}`, {
        method: "POST",
        body: ""
    });

    if (response.status === 409) {
        alert("En fil med det namnet finns redan.");
        return;
    }

    if (!response.ok) {
        alert("Kunde inte skapa fil.");
        return;
    }

    // gör UI:t redo för nästa fil

    input.value = "";

    // uppdaterar fillistan och öppnar den nya filen i editorn

     await loadFiles(currentPath);
    await openFile(currentPath + name);
}

// Skapar en ny mapp baserat på användarens input.
// Hämtar mappnamn via prompt och skickar en POST-request till backend,
// där en bak och fram slash (/) används för att indikera att det är en mapp.
 async function createFolder() {
    const folderName = prompt("Ange mappnamn:");

    if (!folderName) {
        alert("Ange ett mappnamn.");
        return;
    }
    
  // skickar en POST-request till backend för att skapa en ny mapp

  //skickar requesten till backend för att skapa en ny mapp
  //  post skapar en resurs på servern,
  //  i det här fallet en mapp
   const response = await fetch(`/api/files/${currentPath + folderName}/`, {
    method: "POST"
});


console.log("STATUS:", response.status);

const text = await response.text();
console.log("RESPONSE:", text);

    if (response.status === 409) {
        alert("En mapp med det namnet finns redan.");
        return;
    }

    if (!response.ok) {
        alert("Kunde inte skapa mapp.");
        return;
    }

    

    alert("Mapp skapad!");
    await loadFiles(currentPath);
}
function goBack() {

    // om vi redan i root gör inget

    if (!currentPath) return; 
    

    isViewingHistory = false;

    // dela upp path
    const parts = currentPath.split("/").filter(x => x);

    // ta bort sista mappen
    parts.pop();

    // bygg ihop igen
    const newPath = parts.length ? parts.join("/") + "/" : "";

    loadFiles(newPath);
}

saveButton.addEventListener("click", saveFile);
deleteButton.addEventListener("click", deleteFile);
uploadButton.addEventListener("click", uploadFile);

// startar realtidsanslutningen och
//  loggar eventuella fel
// Hanterar navigering bakåt i mappstrukturen genom att ta bort sista delen av currentPath
// och ladda föregående nivå.
// Kopplar även UI-knappar till respektive funktioner (spara, radera, ladda upp).
// Startar SignalR-anslutningen för realtidsuppdateringar mellan klient och server.
// Slutligen laddas initial fillista (root) när applikationen startar.

connection.start()
    .then(() => console.log("SignalR connected"))
    .catch(err => console.error("SignalR error:", err));
// uppdarterar fillistan när sidan laddas
loadFiles();