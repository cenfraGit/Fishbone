(defgroup fishbone nil
  ""
  :group 'languages)

(defcustom fishbone-indent-offset 4
  ""
  :type 'integer
  :group 'fishbone)

(defvar fishbone-mode-syntax-table
  (let ((table (make-syntax-table)))
    ;; strings
    (modify-syntax-entry ?\" "\"" table)

    ;; line comments
    (modify-syntax-entry ?/ ". 124b" table)
    (modify-syntax-entry ?\n "> b" table)

    ;; block comments
    (modify-syntax-entry ?* ". 23" table)

    ;; braces and parens
    (modify-syntax-entry ?{ "(}" table)
    (modify-syntax-entry ?} "){" table)
    (modify-syntax-entry ?\( "()" table)
    (modify-syntax-entry ?\) ")(" table)
    table)
  "syntax table")

(defvar fishbone-highlights
  (let* ((keywords '("if" "else if" "else" "while" "foreach" "in" "func" "return" "break" "continue" "import" "let"))
         (builtins '("true" "false"))
         (operators '("and" "or" "xor" "not"))

         (keywords-regex (regexp-opt keywords 'words))
         (builtins-regex (regexp-opt builtins 'words))
         (operators-regex (regexp-opt operators 'words)))

    `((,keywords-regex . font-lock-keyword-face)
      (,builtins-regex . font-lock-builtin-face)
      (,operators-regex . font-lock-keyword-face)
      ("\\<let\\>\\s-+\\([a-zA-Z_][a-zA-Z0-9_]*\\)" 1 font-lock-variable-name-face)
      ("\\<func\\>\\s-+\\([a-zA-Z_][a-zA-Z0-9_]*\\)" 1 font-lock-function-name-face)
      ("\\<[0-9]+\\(?:\\.[0-9]+\\)?\\>" . font-lock-constant-face)))
  "highlighting rules")

(defun fishbone-indent-line ()
  ""
  (interactive)
  (let ((savep (point))
        (indent-col 0))
    (save-excursion
      (forward-line -1)

      (while (and (looking-at "^[ \t]*$") (= (forward-line -1) 0)))

      (setq indent-col (current-indentation))

      (if (looking-at "^.*{[ \t]*$")
          (setq indent-col (+ indent-col fishbone-indent-offset)))
      )

    (save-excursion
      (beginning-of-line)
      (if (looking-at "^[ \t]*}")
          (setq indent-col (- indent-col fishbone-indent-offset))))

    (if (< indent-col 0) (setq indent-col 0))
    (indent-line-to indent-col)

    (if (< (point) savep)
        (goto-char savep))))

;;;###autoload
(define-derived-mode fishbone-mode prog-mode "Fishbone"
  "Major mode for fishbone"
  :syntax-table fishbone-mode-syntax-table

  (setq-local font-lock-defaults '(fishbone-highlights))

  (setq-local comment-start "// ")
  (setq-local comment-end "")
  (setq-local comment-start-skip "//+\\s-*")

  (setq-local indent-line-function #'fishbone-indent-line)
  (setq-local tab-width fishbone-indent-offset))

;;;###autoload
(add-to-list 'auto-mode-alist '("\\.fishbone\\'" . fishbone-mode))
;;;###autoload
(add-to-list 'auto-mode-alist '("\\.fb\\'" . fishbone-mode))

(provide 'fishbone-mode)