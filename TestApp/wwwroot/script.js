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

async function loadFiles() {
    const response = await fetch("/api/files");
    const data = await response.json();

    fileList.innerHTML = "";

    for (const name in data) {
        const item = document.createElement("div");
        item.className = "file-item";
        item.textContent = name;

        item.addEventListener("click", async () => {
            document.querySelectorAll(".file-item").forEach(x => x.classList.remove("active"));
            item.classList.add("active");
            await openFile(name);
        });

        fileList.appendChild(item);
    }
}

async function openFile(path) {
    currentFile = path;
    fileName.textContent = path;

    const response = await fetch(`/api/files/${path}`);

    if (!response.ok) {
        alert("Kunde inte läsa filen.");
        return;
    }

    const text = await response.text();
    editor.value = text;
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

async function createFile() {
    const input = document.getElementById("newFileName");
    const name = input.value.trim();

    if (!name) {
        alert("Ange ett filnamn.");
        return;
    }

    const response = await fetch(`/api/files/${name}`, {
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

    await loadFiles();
    await openFile(name);
}

saveButton.addEventListener("click", saveFile);
deleteButton.addEventListener("click", deleteFile);
uploadButton.addEventListener("click", uploadFile);

connection.start()
    .then(() => console.log("SignalR connected"))
    .catch(err => console.error("SignalR error:", err));

loadFiles();