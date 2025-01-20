function getCurrentTimeString(e) {
  let t = '';
  let n = e || new Date;
  return t += (n.getFullYear() + "").substring(2) + ";",
    t += getTwoDigit(n.getMonth() + 1) + ";" + getTwoDigit(n.getDate()) + ";" + getTwoDigit(n.getHours()) + ";" + getTwoDigit(n.getMinutes()) + ";" + getTwoDigit(n.getSeconds()) + ";",
    n.getTimezoneOffset() < 0 ? t += "+" + (0 - n.getTimezoneOffset() / 60) : t += 0 - n.getTimezoneOffset() / 60,
    t;
}

function getTwoDigit(e) {
  for (e += ""; e.length < 2;) {
    e = "0" + e;
  }
  return e
}

function dec2hex(e) {
  return (e + 0).toString(16).toUpperCase()
}

function encodeMessage(e) {
  let t = 0
    , n = "";
  if (!e)
    return n;
  e = e.toString();
  for (let o = 0; o < e.length; o++) {
    let r = e.charCodeAt(o);
    if (0 != t) {
      if (56320 <= r && r <= 57343) {
        n += dec2hex(65536 + (t - 55296 << 10) + (r - 56320)),
          t = 0;
        continue
      }
      t = 0
    }
    if (55296 <= r && r <= 56319)
      t = r;
    else {
      for (cp = dec2hex(r); cp.length < 4;)
        cp = "0" + cp;
      n += cp
    }
  }
  return n
}

module.exports = {
  getCurrentTimeString,
  encodeMessage,
}
