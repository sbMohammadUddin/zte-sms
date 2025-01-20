ZTE-SMS is a node.js package that allows you to send SMS messages using ZTE modem.

Tested only with model `MF79U`.

## Install
```bash
npm install @ziga-sebenik/zte-sms
```

## Usage
Construct new instance of the Modem with IP of the modem and modem password:

```js
const myModem = new Modem({ modemIP: 'IP', modemPassword: 'password'});
```

Call `sendSMS` method with phone number of the recipient and sms message.
```js
await myModem.sendSms('phoneNumber', 'message');
```

## Example

```js
const Modem = require('@zigasebenik/zte-sms');
const myModem = new Modem({ modemIP: '192.168.0.1', modemPassword: 'password'});

(async () => {
  try {
    await myModem.sendSms('000000000', 'My message');
  } catch (err) {
    console.error(err);
  }
})();
```