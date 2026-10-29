import { assinaturas, planos, type Campo, type Valores } from '../cenarios';

interface Props {
  campos: Campo[];
  valores: Valores;
  onAlterar: (valores: Valores) => void;
}

/** Os parâmetros que o cenário escolhido aceita. Vazio significa que o serviço sorteia. */
export function CamposDoCenario({ campos, valores, onAlterar }: Props) {
  const alterar = <K extends Campo>(campo: K, valor: Valores[K]) => onAlterar({ ...valores, [campo]: valor });

  return (
    <>
      {campos.includes('quantidade') && (
        <label className="campo campo--curto">
          <span className="campo__rotulo">Quantidade</span>
          <input
            className="campo__controle"
            type="number"
            min={1}
            max={500}
            required
            value={valores.quantidade}
            onChange={(e) => alterar('quantidade', e.target.value)}
          />
        </label>
      )}
      {campos.includes('idade') && (
        <label className="campo campo--curto">
          <span className="campo__rotulo">Idade</span>
          <input
            className="campo__controle"
            type="number"
            min={0}
            max={130}
            placeholder="sorteada"
            value={valores.idade}
            onChange={(e) => alterar('idade', e.target.value)}
          />
        </label>
      )}
      {campos.includes('capitalSegurado') && (
        <label className="campo campo--medio">
          <span className="campo__rotulo">Capital segurado (R$)</span>
          <input
            className="campo__controle"
            type="number"
            min={1}
            step={1000}
            placeholder="sorteado"
            value={valores.capitalSegurado}
            onChange={(e) => alterar('capitalSegurado', e.target.value)}
          />
        </label>
      )}
      {campos.includes('plano') && (
        <label className="campo campo--medio">
          <span className="campo__rotulo">Plano</span>
          <select className="campo__controle" value={valores.plano} onChange={(e) => alterar('plano', e.target.value)}>
            <option value="">Sorteado</option>
            {planos.map((plano) => (
              <option key={plano}>{plano}</option>
            ))}
          </select>
        </label>
      )}
      {campos.includes('assinatura') && (
        <label className="campo campo--medio">
          <span className="campo__rotulo">Assinatura</span>
          <select
            className="campo__controle"
            value={valores.assinatura}
            onChange={(e) => alterar('assinatura', e.target.value)}
          >
            {assinaturas.map((nome) => (
              <option key={nome}>{nome}</option>
            ))}
          </select>
        </label>
      )}
      {campos.includes('simularFalha') && (
        <label className="caixa">
          <input
            type="checkbox"
            checked={valores.simularFalha}
            onChange={(e) => alterar('simularFalha', e.target.checked)}
          />
          Simular falha transitória
        </label>
      )}
    </>
  );
}
