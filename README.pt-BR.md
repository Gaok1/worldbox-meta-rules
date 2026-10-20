# Meta Rules para WorldBox

Crie regras para **um meta individual**: aquele exército, clã, cultura, religião, cidade, reino, idioma, família ou subespécie. As regras não se aplicam automaticamente a todos os metas do mesmo tipo.

Feito para WorldBox 0.51.2 / build 719 com NeoModLoader. A interface usa janelas e abas do jogo, no estilo do Diplomacy Overhaul, com os devidos créditos pela biblioteca visual MIT.

## Como usar

1. Abra a janela normal de um idioma, reino, cultura ou outro meta específico.
2. Clique na nova aba **Meta Rules**, com ícone de opções.
3. O editor abre diretamente naquele meta. Escolha **Restringir**, **Declarar** ou **Visão geral**.

A aba Meta Rules na barra de poderes também permite procurar metas individuais.

### Restringir

Defina quem pode entrar. Cada condição pode exigir um traço ou pertencimento a outro meta específico. Combine TODAS ou QUALQUER condição, com a opção NÃO. Todas as restrições ativas precisam ser atendidas.

Exemplo para a Guarda Imperial:

- Pertence ao clã Casa do Sol.
- Possui o traço Cavaleiro.

As restrições valem para novas entradas. Membros anteriores não são expulsos; aparecem como violações na visão geral quando não atendem às condições.

### Declarar

Defina o que os membros recebem:

- Um traço enquanto pertencerem ao grupo.
- Um traço permanente, que continua após a saída.
- Um bônus de vida, dano, armadura, velocidade, velocidade de ataque, inteligência, habilidade militar, diplomacia ou administração enquanto pertencerem.

Exemplo: todos os falantes de um idioma específico recebem um traço; todos os soldados de um exército recebem +10 de armadura.

Traços anteriores ao benefício são preservados. Se vários grupos concedem o mesmo traço temporário, ele só é retirado quando não houver mais fontes ativas. Traços opostos não são apagados para forçar uma declaração. Os bônus de atributos se somam entre grupos, sem acumular a cada atualização.

### Visão geral

Veja quantos membros existem e quantos não atendem às restrições atuais. Também pode desativar todas as regras daquele meta. Benefícios temporários são retirados nas próximas atualizações; os permanentes continuam.

## Instalação

Extraia o pacote para `worldbox/Mods/MetaRules/`, deixando o `mod.json` diretamente nessa pasta, e reinicie o jogo. O NeoModLoader compila o código ao iniciar.

O idioma acompanha a configuração do jogo: Português (Brasil) ou inglês. Regras ficam no próprio save, junto do meta correspondente.

## Limites da primeira versão

- Não expulsa membros antigos nem transfere automaticamente personagens para outro meta.
- As conquistas não são bloqueadas: os moradores acompanham o reino da cidade conquistada para manter o estado do jogo coerente.
- A atribuição inicial de subespécie não é bloqueada, permitindo que novas criaturas sejam criadas corretamente.
- Declarações de traços e atributos se aplicam aos personagens, e não aos traços do próprio reino, cultura ou idioma.
- O processamento ocorre em lotes; em mundos grandes, mudanças podem levar alguns segundos.
- Compilação e testes do motor foram verificados. O carregamento no jogo, a posição das abas e a interação com os demais mods ainda precisam de teste em partida.

Consulte o [README em inglês](README.md) para os comandos de desenvolvimento e os detalhes de persistência. Código MIT; os créditos da interface estão em [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES).
