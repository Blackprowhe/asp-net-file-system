let currentFile = null;

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/api/events/signalr")
    .build();

const fileList = document.getElementById("fileList");
const fileName = document.getElementById("fileName");
const editor = document.getElementById("editor");
const saveButton = document.getElementById("saveButton");
const deleteButton = document.getElementById("deleteButton");
const uploadButton = document.getElementById("uploadButton");
const uploadFileInput = document.getElementById("uploadFileInput");

connection.on("FileCreated", async function (path) {
    await loadFiles();
});

connection.on("FileUpdated", async function (path) {
    await loadFiles();

    if (currentFile === path) {
        await openFile(path);
    }
});

connection.on("FileDeleted", async function (path) {
    if (currentFile === path) {
        currentFile = null;
        fileName.textContent = "Ingen fil vald";
        editor.value = "";
    }

    await loadFiles();
});

let currentPath = "";

async function loadFiles(path = "") {
    currentPath = path;

    document.getElementById("currentPathDisplay").textContent =
    currentPath ? "/" + currentPath : "/";

    const response = await fetch(`/api/files/${path}`);
const result = await response.json();

// 🔥 FIX
let data = {};

if (result && typeof result === "object") {
    if (result.content && typeof result.content === "object") {
        data = result.content;
    } else {
        data = result;
    }
}
console.log("PATH:", path);
console.log("RESULT:", result);
console.log("DATA:", data);

fileList.innerHTML = "";

// 🔥 visa tom mapp
if (Object.keys(data).length === 0) {
    const empty = document.createElement("div");
    empty.textContent = "(Tom mapp)";
    empty.style.opacity = "0.6";
    empty.style.padding = "10px";
    fileList.appendChild(empty);
    return; // viktigt!
}

// 🔥 annars loopa filer/mappar
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

async function openFile(path) {
    console.log("openFile körs", path);
    currentFile = path;
    fileName.textContent = path;

    const response = await fetch(`/api/files/${path}`);

    if (!response.ok) {
        alert("Kunde inte läsa filen.");
        return;
    }

    // 🔥 om bild
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

    // 🔥 annars text
    const text = await response.text();

    const img = document.getElementById("imagePreview");
    if (img) img.style.display = "none";

    editor.style.display = "block";
    editor.value = text;
    console.log("laddar history för", path);
    loadHistory(path);
}
async function saveFile() {
    if (!currentFile) {
        alert("Välj en fil först.");
        return;
    }

    const response = await fetch(`/api/files/${currentFile}`, {
        method: "PUT",
        headers: {
            "Content-Type": "text/plain"
        },
        body: editor.value
    });

    if (!response.ok) {
        alert("Kunde inte spara filen.");
        return;
    }

    alert("Filen sparades.");
    await loadFiles();
}

async function deleteFile() {
    if (!currentFile) {
        alert("Välj en fil först.");
        return;
    }

    const confirmed = confirm(`Är du säker på att du vill radera filen "${currentFile}"?`);
    if (!confirmed) {
        return;
    }

    const response = await fetch(`/api/files/${currentFile}`, {
        method: "DELETE"
    });

    if (!response.ok) {
        alert("Kunde inte radera filen.");
        return;
    }

    alert("Filen raderades.");
    currentFile = null;
    fileName.textContent = "Ingen fil vald";
    editor.value = "";
    await loadFiles();
}

async function uploadFile() {
    const file = uploadFileInput.files[0];
    if (!file) {
        alert("Välj en fil att ladda upp.");
        return;
    }

    const content = await file.text();

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
async function loadHistory(path) {
    const res = await fetch(`/api/files/history/${path}`);
    const history = await res.json();

    const container = document.getElementById("history");
    container.innerHTML ="";

    history.forEach(version => {
        const btn = document.createElement("button");
         btn.innerText = `Version ${version.version}`;

         btn.onclick = () => {
            document.getElementById("editor").value = version.content;
         };

         container.appendChild(btn);
    });
}
async function createFile() {
    const input = document.getElementById("newFileName");
    const name = input.value.trim();

    if (!name) {
        alert("Ange ett filnamn.");
        return;
    }
    
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

    input.value = "";

     await loadFiles(currentPath);
    await openFile(currentPath + name);
}

 async function createFolder() {
    const folderName = prompt("Ange mappnamn:");

    if (!folderName) {
        alert("Ange ett mappnamn.");
        return;
    }
    

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
    await loadFiles();
}
function goBack() {
    if (!currentPath) return; // redan i root

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

connection.start()
    .then(() => console.log("SignalR connected"))
    .catch(err => console.error("SignalR error:", err));

loadFiles();