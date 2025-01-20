const merge = require('lodash.merge');
const http = require('http');
const qs = require('qs');
const { getCurrentTimeString, encodeMessage } = require('./utils');
const { hex_md5 } = require('./md5');

class Modem {
  constructor({ modemIP = '192.168.0.1', modemPassword = ''} = {}) {
    this.modemIP = modemIP;
    this.modemPassword = modemPassword
    this.getPath = `/goform/goform_get_cmd_process`;
    this.setPath = `/goform/goform_set_cmd_process`;
    this.referer = `http://${this.modemIP}/index.html`;

    this.loginCookieValue = '';
    this.modemVersion = '';
  }

  async #request(options, data) {
    const headers = { Referer: this.referer };
    data = data ? qs.stringify(data) : null;
    data && (headers['Content-Length'] = data.length);

    options = merge(options, {
      hostname: this.modemIP,
      insecureHTTPParser: true,
      headers,
    });

    return new Promise((resolve, reject) => {
      const request = http.request(options, (response) => {
        let data = '';
        response.on('data', (chunk) => {
          data += chunk.toString();
        });
        response.on('end', () => {
          resolve({ response, data: JSON.parse(data) });
        });
        response.on('error', (error) => {
          reject(error);
        });
      });
      data && request.write(data);
      request.end();
    });
  }

  async #login() {
    const data = {
      isTest: false,
      goformId: 'LOGIN',
      password: Buffer.from(this.modemPassword).toString('base64'),
    };
    const options = { method: 'POST', path: this.setPath };
    const response = await this.#request(options, data);
    this.loginCookieValue = response.response.headers['set-cookie'][0].split(';')[0];
  }

  async #logout() {
    const data = {
      isTest: false,
      goformId: 'LOGOUT',
      AD: await this.#getAD(),
    };
    const options = { method: 'POST', path: this.setPath };
    const response = await this.#request(options, data);
    this.loginCookieValue = '';
    return response.data.result === 'success'
  }

  async #getModemVersion() {
    if (this.modemVersion) {
      return this.modemVersion;
    }
    const options = {
      method: 'GET',
      path: `${this.getPath}?isTest=false&cmd=cr_version,wa_inner_version&multi_data=1`,
    };
    const response = await this.#request(options);
    if (response.data.wa_inner_version || response.data.cr_version) {
      this.modemVersion = response.data?.cr_version + response.data?.wa_inner_version;
      return this.modemVersion;
    } else {
      throw new Error('Modem version failed');
    }
  }

  async #getAD() {
    const modemVersion = await this.#getModemVersion();
    const RD = await this.#getRD();
    return hex_md5(`${hex_md5(modemVersion)}${RD}`);
  }

  async #getRD() {
    const options = {
      method: 'GET',
      path: `${this.getPath}?isTest=false&cmd=RD`,
    };
    const response = await this.#request(options);

    if (response.data.RD) {
      return response.data.RD;
    } else {
      throw new Error('RD failed');
    }
  }

  async sendSms(number, message) {
    await this.#login();

    const data = {
      isTest: false,
      goformId: 'SEND_SMS',
      notCallback: true,
      Number: number.toString(),
      sms_time: getCurrentTimeString(),
      MessageBody: encodeMessage(message),
      ID: -1,
      encode_type: 'UNICODE',
      AD: await this.#getAD(),
    };
    const options = {
      method: 'POST',
      path: this.setPath,
      headers: { Cookie: this.loginCookieValue },
    };
    const response = await this.#request(options, data);
    await this.#logout();
    return response.data.result === 'success'
  }
}

module.exports = Modem;
