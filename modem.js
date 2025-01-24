const { setTimeout } = require("timers/promises");
const merge = require('lodash.merge');
const http = require('http');
const qs = require('qs');
const { hex_md5 } = require('./md5');
const {
  getCurrentTimeString,
  encodeMessage,
  decodeMessage,
  transTime
} = require('./utils');

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

  async #request(options, data = '') {
    const headers = { Referer: this.referer };

    data = qs.stringify(data);
    options.path = this.getPath;
    if (options.method === 'POST') {
      options.path = this.setPath;
      headers['Content-Length'] = data.length;
    } else if (options.method === 'GET') {
      options.path += `?${data}`;
      data = '';
    }

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
      request.on('error', (error) => { throw new Error(error); });
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
    const options = { method: 'POST' };
    const response = await this.#request(options, data);
    if (response.data.result !== '0') {
      throw new Error('Logion to modem failed.');
    }
    this.loginCookieValue = response.response.headers['set-cookie'][0].split(';')[0];
  }

  async #logout() {
    const data = {
      isTest: false,
      goformId: 'LOGOUT',
      AD: await this.#getAD(),
    };
    const options = { method: 'POST' };
    await this.#request(options, data);
    this.loginCookieValue = '';
  }

  async #getModemVersion() {
    if (this.modemVersion) {
      return this.modemVersion;
    }
    const data = {
      isTest: false,
      cmd: 'cr_version,wa_inner_version',
      multi_data: 1,
    }
    const options = { method: 'GET' };
    const response = await this.#request(options, data);
    if (response.data.wa_inner_version || response.data.cr_version) {
      this.modemVersion = `${response.data?.cr_version}${response.data?.wa_inner_version}`;
      return this.modemVersion;
    } else {
      throw new Error('Getting modem version failed.');
    }
  }

  async #getAD() {
    const modemVersion = await this.#getModemVersion();
    const RD = await this.#getRD();
    return hex_md5(`${hex_md5(modemVersion)}${RD}`);
  }

  async #getRD() {
    const data = {
      isTest: false,
      cmd: 'RD',
    }
    const options = { method: 'GET' };
    const response = await this.#request(options, data);

    if (!response.data.RD) {
      throw new Error('Getting RD failed.');
    }
    return response.data.RD;
  }

  async getSmsCapacityInfo() {
    await this.#login();
    const data = {
      isTest: false,
      cmd: 'sms_capacity_info',
    };
    const options = {
      method: 'GET',
      headers: { Cookie: this.loginCookieValue },
    };
    const response = await this.#request(options, data);
    await this.#logout();
    return response.data;
  }

  async getAllSms() {
    await this.#login();
    const data = {
      isTest: false,
      cmd: 'sms_data_total',
      page: 0,
      data_per_page: 5000,
      mem_store: 1,
      tags: 10,
      order_by: 'order by id desc',
    };
    const options = {
      method: 'GET',
      headers: { Cookie: this.loginCookieValue },
    };
    const response = await this.#request(options, data);
    await this.#logout();
    response.data.messages.forEach((m) => {
      m.date = transTime(m.date, '3', '24');
      m.content = decodeMessage(m.content);
    });
    return response.data.messages;
  }

  async deleteSms(ids) {
    ids = Array.isArray(ids) ? ids : Array(ids);
    await this.#login();
    const data = {
      isTest: false,
      goformId: 'DELETE_SMS',
      msg_id: `${ids.join(';')};`,
      notCallback: true,
      AD: await this.#getAD(),
    };
    const options = {
      method: 'POST',
      headers: { Cookie: this.loginCookieValue },
    };
    const response = await this.#request(options, data);
    await this.#logout();
    if (response.data.result !== 'success') {
      throw new Error('Error deleting SMS.');
    }
  }


  // sms tags
  // 1	Unread received message.
  // 0	Received message.
  // 2	Message sent.
  // 3	Message failed to be sent.
  // 4	Draft.
  async deleteAllSms(tag = '') {
    tag = tag.toString();
    let messages = await this.getAllSms();
    messages = messages.filter((m) => !tag || m.tag === tag);
    const messageIds = messages.map((m) => m.id);
    return await this.deleteSms(messageIds);
  }

  async setSmsAsRead(ids) {
    ids = Array.isArray(ids) ? ids : Array(ids);
    await this.#login();
    const data = {
      isTest: false,
      goformId: 'SET_MSG_READ',
      msg_id: `${ids.join(';')};`,
      tag: '0',
      AD: await this.#getAD(),
    };
    const options = {
      method: 'POST',
      headers: { Cookie: this.loginCookieValue },
    };
    const response = await this.#request(options, data);
    await this.#logout();
    await setTimeout(300);
    if (response.data.result !== 'success') {
      throw new Error('Error marking SMS as read.');
    }
  }

  async setAllSmsAsRead() {
    let messages = await this.getAllSms();
    messages = messages.filter((m) => m.tag === '1');
    const messageIds = messages.map((m) => m.id);
    return await this.setSmsAsRead(messageIds);
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
      headers: { Cookie: this.loginCookieValue },
    };
    const response = await this.#request(options, data);
    await this.#logout();
    if (response.data.result !== 'success') {
      throw new Error('Error sending sending SMS.');
    }
  }
}

module.exports = Modem;
