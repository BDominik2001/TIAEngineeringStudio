# AI-asszisztens (tervezett)

Helyfoglaló a verziókövetési szabályzat szerinti `src/python/ai/` modulnak. Még nincs implementálva,
és a jelenlegi kiadás semmilyen AI-szolgáltatást nem hív.

Tervezési korlátok, ha elkészül:

- csak javaslatot tehet a gépmodellre vagy a generált forrásra; a munkafolyamat (validálás, statikus
  ellenőrzés, átnézés, jóváhagyás, sandbox-import) ugyanúgy kötelező marad;
- nem kaphat hozzáférést TIA-projekthez, PLC-hez, licenchez vagy titkokhoz;
- a hívott modell, a prompt és a válasz SHA-256-tal kerül a műveleti naplóba.
