const string = "1234567890QWERTYUIOPASDFGHJKLZXCVBNMqwertyuiopasdfghjklzxcvbnm";

export function randomString(length) {
    const output = Array(length);
    for (let i = 0; i < length; i++) output[i] = string[Math.floor(Math.random() * string.length)];
    return output.join("");
}